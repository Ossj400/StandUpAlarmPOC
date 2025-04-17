using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AndroidX.Annotations;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using static Android.Webkit.WebStorage;

namespace StandUpAlarmPOC.Platforms.Android.Services
{
    public class PlateOCRService

    {
        private const int DetectorInputWidth = 384;
        private const int DetectorInputHeight = 384;
        private const int OcrInputWidth = 96;
        private const int OcrInputHeight = 48;

        private InferenceSession detectorSession;
        private InferenceSession ocrSession;

        public PlateOCRService(string detectorModelPath, string ocrModelPath)
        {
            detectorSession =  LoadModelFromAssets(detectorModelPath).Result;
            ocrSession = LoadModelFromAssets(ocrModelPath).Result;
        }
        public async Task<InferenceSession> LoadModelFromAssets(string fileName)
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return new InferenceSession(memoryStream.ToArray()); // Load model from byte[]
        }
        public SKBitmap LoadAndResize(string imagePath, int width, int height)
        {
            using var inputStream = File.OpenRead(imagePath);
            using var original = SKBitmap.Decode(inputStream);
            return original.Resize(new SKImageInfo(width, height), SKFilterQuality.High);
        }
        public async Task<SKBitmap> LoadAndResizeAsync(string assetFileName, int width, int height)
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(assetFileName);
            var original = SKBitmap.Decode(stream);

            if (original == null)
                throw new Exception("Failed to decode image from asset: " + assetFileName);

            return original.Resize(new SKImageInfo(width, height), SKFilterQuality.High);
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

        public byte[] PrepareImageForOCR(SKBitmap croppedPlate)
        {
            int width = 140;
            int height = 70;
            var resized = croppedPlate.Resize(new SKImageInfo(width, height), SKFilterQuality.High);
            byte[] tensorData = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = resized.GetPixel(x, y);

                    // Convert to grayscale (using luminosity method)
                    byte gray = (byte)(0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue);
                    tensorData[y * width + x] = gray;
                }
            }

            return tensorData;
        }

        public float[] PrepareImageForDetection(SKBitmap image)
        {
            int width = image.Width;
            int height = image.Height;
            float[] tensorData = new float[1 * 3 * height * width];

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

            return tensorData;
        }

        public List<SKRectI> DetectPlates(SKBitmap image)
        {
            var meta = ocrSession.InputMetadata.First();

            float[] inputData = PrepareImageForDetection(image);
            var tensor = new DenseTensor<float>(inputData, new[] { 1, 3, image.Height, image.Width });
            var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor(detectorSession.InputMetadata.Keys.First(), tensor)
        };

            using var results = detectorSession.Run(inputs);
            var output = results.First().AsTensor<float>();

            var boxes = new List<SKRectI>();

            int numDetections = output.Dimensions[0];
            for (int i = 0; i < numDetections; i++)
            {
                float confidence = output[i, 4];

                if (confidence > 0.5f)
                {
                    float cx = output[i, 0] * image.Width;
                    float cy = output[i, 1] * image.Height;
                    float w = output[i, 2] * image.Width;
                    float h = output[i, 3] * image.Height;

                    int x1 = (int)(cx - w / 2);
                    int y1 = (int)(cy - h / 2);
                    int x2 = (int)(cx + w / 2);
                    int y2 = (int)(cy + h / 2);

                    boxes.Add(new SKRectI(x1, y1, x2, y2));
                }
            }

            return boxes;
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
            var floatPredicted = output.ToArray();
            var intPredicted = floatPredicted.Select(f => (int)Math.Round(f)).ToArray(); // 👈 safe conversion

            string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_";

            var result = new List<char>();
            int? last = null;

            foreach (var index in intPredicted)
            {
                if (index == last || index >= alphabet.Length || alphabet[index] == '_')
                    continue;

                result.Add(alphabet[index]);
                last = index;
            }

            var text = new string(result.ToArray());
            return text;
        }

        public async Task<(ImageSource image, string text)> Run(string imagePath)
        {
            var resizedImage = await LoadAndResizeAsync(imagePath, 384, 384);
            var boxes = DetectPlates(resizedImage);

            foreach (var box in boxes)
            {
                var plateImage = Crop(resizedImage, box);
                if (plateImage != null)
                {
                    var text = RecognizeText(plateImage);
                    var image = ConvertSKBitmapToImageSource(plateImage);
                    return (image, text);
                }
            }

            return (null, "No plates detected");
        }

        public static DenseTensor<byte> PrepareGrayscaleTensor(SKBitmap croppedPlate, int width = 140, int height = 70)
        {
            try
            {
                // Resize the plate to model input size
                var resized = croppedPlate.Resize(new SKImageInfo(width, height), SKFilterQuality.High);

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
