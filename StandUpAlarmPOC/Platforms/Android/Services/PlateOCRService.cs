using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AndroidX.Annotations;
using Java.Util.Streams;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace StandUpAlarmPOC.Platforms.Android.Services
{
    public class PlateOCRService

    {
        private InferenceSession detectorSession;
        private InferenceSession ocrSession;
        private ImageSource previewImage;
        public Action<ImageSource> OnPreviewFrameChanged;
        public Action<string> OnTextChanged;
        private string _detectorModelPath;
        private string _ocrModelPath;

        public PlateOCRService(string detectorModelPath, string ocrModelPath)
        {
            _detectorModelPath = detectorModelPath;
            _ocrModelPath = ocrModelPath;
        }
        public async Task InitializeAsync()
        {
            detectorSession = await LoadModelFromAssets(_detectorModelPath);
            ocrSession = await LoadModelFromAssets(_ocrModelPath);
        }
        public async Task<InferenceSession> LoadModelFromAssets(string fileName)
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return new InferenceSession(memoryStream.ToArray()); // Load model from byte[]
        }

        public SKBitmap Crop(SKBitmap image, SKRectI cropRect)
        {

            int x1 = Math.Min(cropRect.Left, cropRect.Right);
            int y1 = Math.Min(cropRect.Top, cropRect.Bottom);
            int x2 = Math.Max(cropRect.Left, cropRect.Right);
            int y2 = Math.Max(cropRect.Top, cropRect.Bottom);

            // Completely discard if out of bounds
            if (x2 < 0 || y2 < 0 || x1 > image.Width || y1 > image.Height)
            {
                Console.WriteLine("🚫 Crop box is completely outside the image.");
                return null;
            }
            // Clamp to image bounds
            x1 = Math.Max(0, Math.Min(image.Width, x1));
            y1 = Math.Max(0, Math.Min(image.Height, y1));
            x2 = Math.Max(0, Math.Min(image.Width, x2));
            y2 = Math.Max(0, Math.Min(image.Height, y2));

            int width = x2 - x1;
            int height = y2 - y1;

            if (width <= 0 || height <= 0)
            {
                Console.WriteLine($"🚫 Invalid crop after clamping: {width}x{height}");
                return null;
            }

            var cropped = new SKBitmap(width, height);
            using var canvas = new SKCanvas(cropped);

            canvas.DrawBitmap(image, new SKRect(x1, y1, x2, y2), new SKRect(0, 0, width, height));

            return cropped;
        }

        public DenseTensor<float> PrepareImageForDetection(SKBitmap image)
        {
            int width = image.Width;
            int height = image.Height;
            int channels = 3;

            float[] tensorData = new float[1 * channels * height * width];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    int offset = y * width + x;

                    tensorData[offset] = pixel.Red / 255f;
                    tensorData[height * width + offset] = pixel.Green / 255f;
                    tensorData[2 * height * width + offset] = pixel.Blue / 255f;
                }
            }

            return new DenseTensor<float>(tensorData, new[] { 1, 3, height, width }); // ✅ NCHW
        }

        public static ImageSource DetectionTensorToImage(float[] tensorData, int width, int height)
        {
            var bitmap = new SKBitmap(width, height);

            int channelSize = width * height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = y * width + x;

                    byte r = (byte)(tensorData[offset] * 255);
                    byte g = (byte)(tensorData[channelSize + offset] * 255);
                    byte b = (byte)(tensorData[2 * channelSize + offset] * 255);

                    bitmap.SetPixel(x, y, new SKColor(r, g, b));
                }
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());

            return ImageSource.FromStream(() => new MemoryStream(stream.ToArray()));
        }

        public List<SKRectI> DetectPlates(SKBitmap image)
        {
            var meta = ocrSession.InputMetadata.First();
            var tensor = PrepareImageForDetection(image);

            var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor(detectorSession.InputMetadata.Keys.First(), tensor)
        };

            using var results = detectorSession.Run(inputs);
            var output = results.First().AsTensor<float>();
            var boxes = new List<SKRectI>();
            int numDetections = output.Dimensions[0];
            for (int i = 0; i < numDetections; i++)
            {
                float confidence = output[i, 6];
                if (confidence > 0.5f)
                {
                    int x1 = (int)Math.Round(output[i, 1]);
                    int y1 = (int)Math.Round(output[i, 2]);
                    int x2 = (int)Math.Round(output[i, 3]);
                    int y2 = (int)Math.Round(output[i, 4]);

                    boxes.Add(new SKRectI(x1, y1, x2, y2));
                }
            }

            return boxes;
        }

        public string DecodeWithoutCollapse(Tensor<float> output)
        {
            string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
            var flat = output.ToArray();

            int vocabSize = alphabet.Length;
            int timeSteps = flat.Length / vocabSize;

            var result = new List<char>();

            for (int t = 0; t < timeSteps; t++)
            {
                // this float.MinValue is big issue but now fixing the thing with spacing 
                float bestScore = float.MinValue;
                int bestIndex = 0;

                for (int c = 0; c < vocabSize; c++)
                {
                    float score = flat[t * vocabSize + c];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = c;
                    }
                }

                if (alphabet[bestIndex] != '_') // just skip blanks
                    result.Add(alphabet[bestIndex]);
            }

            return new string(result.ToArray());
        }
        public string RecognizeText(SKBitmap croppedPlate)
        {
            var tensor = PrepareGrayscaleTensor(croppedPlate);
            var inputName = ocrSession.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(inputName, tensor)
                };

            using var results = ocrSession.Run(inputs);
            var output = results.First().AsTensor<float>();

            return DecodeWithoutCollapse(output);
        }

        public async Task<(ImageSource image, string text)> Run(SKBitmap imageSkBitmp)
        {
            var text = RecognizeText(imageSkBitmp);
            previewImage = ConvertSKBitmapToImageSource(imageSkBitmp);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPreviewFrameChanged?.Invoke(previewImage);
                OnTextChanged?.Invoke(text);
            });
            return (null, "No plates detected");
        }

        public static DenseTensor<byte> PrepareGrayscaleTensor(SKBitmap croppedPlate, int width = 140, int height = 70)
        {
            try
            {
                // Resize the plate to model input size
                var resized = croppedPlate.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);

                // Allocate byte array for grayscale pixels
                byte[] tensorData = new byte[width * height];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = resized.GetPixel(x, y);

                        // Convert to grayscale using luminosity method
                        byte gray = (byte)(0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue);
                        tensorData[y * width + x] = gray;
                    }
                }

                // Safety check
                if (tensorData.Length != width * height)
                    throw new Exception($"Tensor length {tensorData.Length} doesn't match shape {width}x{height}");

                // Create tensor in NHWC format: [1, height, width, 1]
                var dimensions = new int[] { 1, height, width, 1 };
              //  Console.WriteLine($"First few grayscale bytes: {string.Join(", ", tensorData.Take(50))}");
              //  previewImage = TensorToImageSource(tensorData, 140, 70);

                return new DenseTensor<byte>(tensorData, dimensions);
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
            }
            return null;
        }
        public ImageSource ConvertSKBitmapToImageSource(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            return ImageSource.FromStream(() => new MemoryStream(stream.ToArray()));
        }

    }
}
