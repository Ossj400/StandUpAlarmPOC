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
        private static ImageSource previewImage;

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

        public async Task<SKBitmap> LoadAndResizeAsync(string assetFileName, int width, int height)
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(assetFileName);
            var original = SKBitmap.Decode(stream);

            if (original == null)
                throw new Exception("Failed to decode image from asset: " + assetFileName);

            return original.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
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
            var resized = croppedPlate.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
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
           // previewImage = DetectionTensorToImage(inputData, image.Width, image.Height);

            var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor(detectorSession.InputMetadata.Keys.First(), tensor)
        };

            using var results = detectorSession.Run(inputs);
            var output = results.First().AsTensor<float>();
            Console.WriteLine($"OCR Output shape: {string.Join(" x ", output.Dimensions.ToArray())}");

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
        public string DecodeOcrFlatOutput(Tensor<float> output)
        {
            string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_";

            var flat = output.ToArray();
            var indices = flat.Select(f => (int)Math.Round(f)).ToList();

            var result = new List<char>();
            int? last = null;

            foreach (var index in indices)
            {
                if (index >= alphabet.Length) continue;

                char current = alphabet[index];

                if (current == '_' || index == last)
                    continue;

                result.Add(current);
                last = index;
            }

            return new string(result.ToArray());
        }
        public string DecodeSoftmaxFlatOutput(Tensor<float> output)
        {
            string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
            var flat = output.ToArray();

            int vocabSize = alphabet.Length; // 37
            int timeSteps = flat.Length / vocabSize;

            var result = new List<char>();
            int? lastIndex = null;

            for (int t = 0; t < timeSteps; t++)
            {
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

                // CTC-style filtering
                if (bestIndex != lastIndex && alphabet[bestIndex] != '_')
                {
                    result.Add(alphabet[bestIndex]);
                    lastIndex = bestIndex;
                }
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
            Console.WriteLine("OCR output shape: " + string.Join(" x ", output.Dimensions.ToArray())); 
            Console.WriteLine("OCR raw output:");
            Console.WriteLine(string.Join(", ", output.ToArray()));
            var floatPredicted = output.ToArray();
            string text = DecodeSoftmaxFlatOutput(output);
            Console.WriteLine("OCR raw output indices:");
            Console.WriteLine(string.Join(", ", output.ToArray().Take(30)));
            var intPredicted = floatPredicted.Select(f => (int)Math.Round(f)).ToArray(); // 👈 safe conversion

            string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
            var meta = ocrSession.InputMetadata[inputName];
            Console.WriteLine($"OCR Input shape: {string.Join(" x ", meta.Dimensions)}");
            var result = new List<char>();
            int? last = null;

            foreach (var index in intPredicted)
            {
                if (index == last || index >= alphabet.Length || alphabet[index] == '_')
                    continue;

                result.Add(alphabet[index]);
                last = index;
            }

            var textx = new string(result.ToArray());
            return text;
        }

        public async Task<(ImageSource image, string text)> Run(string imagePath)
        {
            var resizedImage = await LoadAndResizeAsync(imagePath, 384, 384);
            //return (ConvertSKBitmapToImageSource(resizedImage), "bad");
            //  var ru = Run(resizedImage);
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
        public static ImageSource TensorToImageSource(byte[] tensorData, int width, int height)
        {
            // Create grayscale bitmap
            var bitmap = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);

            // Fill bitmap pixel buffer
            var pixels = bitmap.GetPixelSpan();
            if (tensorData.Length != pixels.Length)
                throw new Exception("Tensor data size does not match bitmap dimensions.");

            tensorData.CopyTo(pixels);

            // Convert to MAUI ImageSource
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());

            return ImageSource.FromStream(() => new MemoryStream(stream.ToArray()));
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
                Console.WriteLine($"First few grayscale bytes: {string.Join(", ", tensorData.Take(50))}");
                previewImage = TensorToImageSource(tensorData, 140, 70);

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

        public (string PlateText, ImageSource PlateImage, ImageSource AnnotatedImage) Run(SKBitmap originalBitmap)
        {
            if (originalBitmap == null)
                throw new ArgumentNullException(nameof(originalBitmap));



            // 1. Detect license plate regions in the original image
            var plateRegions = DetectPlates(originalBitmap);

            // 2. Draw red bounding boxes on the original image for each detected region
            if (plateRegions.Count > 0)
            {
                using (var canvas = new SKCanvas(originalBitmap))
                using (var paint = new SKPaint())
                {
                    paint.Color = SKColors.Red;
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 3;
                    paint.IsAntialias = true;
                    foreach (var region in plateRegions)
                    {
                        // region is assumed to be an SKRect (or convertible to SKRect) for the plate's bounding box
                        SKRect rect = region;  // use directly if region is SKRect
                                               // If region is in another format (e.g., Rect with int coordinates), convert to SKRect:
                                               // SKRect rect = SKRect.Create(region.X, region.Y, region.Width, region.Height);
                        canvas.DrawRect(rect, paint);
                    }
                }
                // After disposing the canvas, the originalBitmap now has the rectangles drawn on it
            }

            // 3. Prepare the annotated original image as an ImageSource (for display in UI)
            ImageSource annotatedImageSource;
            using (SKImage annotatedImage = SKImage.FromBitmap(originalBitmap))
            {
                // Encode to PNG (or JPEG) format in memory
                using SKData data = annotatedImage.Encode(SKEncodedImageFormat.Png, 100);
                // Create a new stream from the encoded data for the ImageSource
                byte[] imageBytes = data.ToArray();
                annotatedImageSource = ImageSource.FromStream(() => new MemoryStream(imageBytes));
            }

            // 4. If at least one plate was detected, process the first plate for OCR
            string plateText = string.Empty;
            ImageSource plateImageSource = null;
            if (plateRegions.Count > 0)
            {
                // Take the first detected plate region
                SKRectI firstRegion = plateRegions[0];
                // Crop the plate region from the original image
                SKBitmap plateBitmap = new SKBitmap();
                originalBitmap.ExtractSubset(plateBitmap, firstRegion);
                if (Crop(originalBitmap, firstRegion) == null)
                    return (plateText, plateImageSource, annotatedImageSource);
                plateBitmap = Crop(originalBitmap, firstRegion);
                using (var canvasCrop = new SKCanvas(plateBitmap))
                {
                    // Draw the portion of the original image corresponding to the plate region onto plateBitmap
                    canvasCrop.DrawBitmap(originalBitmap, firstRegion, new SKRect(0, 0, firstRegion.Width, firstRegion.Height));
                }

                // 5. Preprocess the cropped plate image (grayscale, threshold, etc.)
                // (This uses the existing preprocessing pipeline – not modified)
                SKBitmap processedPlate = Crop(originalBitmap, firstRegion);

                // 6. Perform OCR on the processed plate image to get the text
                plateText = RecognizeText(processedPlate);

                // 7. Convert the processed plate image to ImageSource for UI display
                using (SKImage plateImage = SKImage.FromBitmap(processedPlate))
                {
                    using SKData plateData = plateImage.Encode(SKEncodedImageFormat.Png, 100);
                    byte[] plateBytes = plateData.ToArray();
                    plateImageSource = ImageSource.FromStream(() => new MemoryStream(plateBytes));
                }
            }

            // Return the result: recognized text, grayscale plate image, and annotated original image
            return (plateText, plateImageSource, annotatedImageSource);
        }


    }
}
