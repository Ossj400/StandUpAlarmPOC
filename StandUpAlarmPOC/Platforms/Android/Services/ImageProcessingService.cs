using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Android.Content.Res;
using Java.Util.Streams;
using SkiaSharp;
using StandUpAlarmPOC.Interfaces;
using static System.Net.Mime.MediaTypeNames;
using static Android.Resource;
using static Java.Util.Jar.Attributes;

namespace StandUpAlarmPOC.Platforms.Android.Services
{
    public class ImageProcessingService : IImageProcessing
    {

        public Action<ImageSource> OnImageReady;
        public Action<ImageSource> OnTxtReady;
        private PlateOCRService ocr;
        
        public ImageProcessingService()
        {
            InitializeOCR();
        }
        private async void InitializeOCR()
        {

            ocr = new("yolo-v9-t-640-license-plates-end2end.onnx", "european_mobile_vit_v2_ocr.onnx");
            await ocr.InitializeAsync();

        }
        public void HandlePlates(List<string> plates)
        {
            // Do something with the result here`
            Console.WriteLine("Plates received: " + string.Join(", ", plates));
        }

        public async Task<(ImageSource image, string text)> ProcessUploadedImage(Stream imageStream, Action<ImageSource> onFrameUpdate, Action<string> txtUpdate)
        {
            try
            {
                MemoryStream memoryStream = new MemoryStream();
                await imageStream.CopyToAsync(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);


                // Replace with your actual path if needed
                bool useOnDebug = false;

                ImageSource imageRet = null;
                string text = "";

                if (useOnDebug)
                {
                    string[] fileNames = new[]
                    {
                        "PXL_20250425_112903522.jpg",
                        "PXL_20250425_112907288.jpg",
                        "PXL_20250425_112910031.jpg",
                        "PXL_20250425_112911763.jpg",
                        "PXL_20250425_112918470.jpg",
                        "PXL_20250425_112924317.jpg",
                        "PXL_20250425_112925847.jpg",
                        "PXL_20250425_112932534.jpg",
                        "PXL_20250425_113012164.MP.jpg",
                        "PXL_20250425_113037102.MP.jpg",
                        "PXL_20250425_113038125.jpg",
                        "PXL_20250425_113039898.MP.jpg",
                        "PXL_20250425_113041819.MP.jpg",
                        "PXL_20250425_113054619.MP.jpg",
                        "PXL_20250425_113103570.jpg",
                        "PXL_20250425_113125992.MP.jpg",
                        "PXL_20250425_113128549.MP.jpg",
                        "PXL_20250425_113129927.jpg",
                        "PXL_20250425_113204473.jpg",
                        "PXL_20250425_113303477.MP.jpg",
                        "PXL_20250425_113307401.MP.jpg",
                        "PXL_20250425_113341688.MP.jpg",
                        "PXL_20250425_113344436.jpg",
                        "PXL_20250425_113345785.jpg",
                        "PXL_20250425_113346672.jpg",
                        "PXL_20250425_113350421.jpg",
                        "PXL_20250425_113351014.jpg",
                        "PXL_20250425_113400237.MP.jpg",
                        "PXL_20250425_113414723.MP.jpg",
                        "PXL_20250425_113416781.MP.jpg",
                        "PXL_20250425_113428159.MP.jpg",
                        "PXL_20250425_113453361.MP.jpg",
                        "PXL_20250425_113454464.jpg",
                        "PXL_20250425_113602321.MP.jpg",
                        "PXL_20250425_113703902.MP.jpg",
                        "PXL_20250425_113706906.MP.jpg",
                        "PXL_20250425_113718842.jpg",
                        "PXL_20250425_113724290.jpg",
                        "PXL_20250425_113736949.MP.jpg",
                        "PXL_20250425_113816290.jpg"
                    };

                    foreach (var filePath in fileNames)
                    {
                        using var fileStream = await FileSystem.OpenAppPackageFileAsync(filePath);
                        memoryStream = new MemoryStream();
                        await fileStream.CopyToAsync(memoryStream);
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        RunOCRProcess(memoryStream, onFrameUpdate, txtUpdate);
                        /* var croppedPlates = DetectAndCropFullResolutionPlates(memoryStream);

                         foreach (var plateCrop in croppedPlates)
                         {
                             (imageRet, text) = await ocr.Run(plateCrop);
                             ocr.OnPreviewFrameChanged = onFrameUpdate;
                             ocr.OnTextChanged = txtUpdate;
                         }
                        */
                    }
                }
                else
                {
                    var retVariables = await RunOCRProcess(memoryStream, onFrameUpdate, txtUpdate);
                    return retVariables;
                }


                return (null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR error: {ex.Message}");
                return (null, "No plates detected");

            }
        }
        public async Task<(ImageSource, string)> RunOCRProcess(MemoryStream memoryStream, Action<ImageSource> onFrameUpdate, Action<string> txtUpdate)
        {
            string text ="";
            ImageSource imageRet = null;
            List<string> plates = new List<string>();

            try
            {
                var croppedPlates = DetectAndCropFullResolutionPlates(memoryStream);
                foreach (var plateCrop in croppedPlates)
                {
                    (imageRet, text) = await ocr.Run(plateCrop);
                    plates.Add(text);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR error: {ex.Message}");
            }
            return (imageRet, string.Join("|", plates) + "Counted: "+ plates.Count);

        }
        public List<SKBitmap> DetectAndCropFullResolutionPlates(MemoryStream highResStream, float detectionResizeWidth = 1280f, int tileSize = 640, float overlap = 0.25f, float paddingPercent = 0.1f)
        {
            var croppedPlates = new List<SKBitmap>();

            // Decode original full-res image
            var original = SKBitmap.Decode(highResStream);
            if (original == null) return croppedPlates;

            // Resize for detection (e.g., 1280px wide)
            int detectionWidth = (int)detectionResizeWidth;
            int detectionHeight = (int)(original.Height * (detectionWidth / (float)original.Width));
            var detectionImage = original.Resize(new SKImageInfo(detectionWidth, detectionHeight), SKFilterQuality.Medium);

            // Calculate scaling factors
            float scaleX = original.Width / (float)detectionImage.Width;
            float scaleY = original.Height / (float)detectionImage.Height;

            int step = (int)(tileSize * (1f - overlap));
            if (step <= 0) step = 1;

            // Slide windows over detection image
            for (int y = 0; y + tileSize <= detectionImage.Height; y += step)
            {
                for (int x = 0; x + tileSize <= detectionImage.Width; x += step)
                {
                    // Extract 640x640 tile
                    var tile = new SKBitmap(tileSize, tileSize);
                    using (var canvas = new SKCanvas(tile))
                    {
                        var src = new SKRectI(x, y, x + tileSize, y + tileSize);
                        canvas.DrawBitmap(detectionImage, src, new SKRect(0, 0, tileSize, tileSize));
                    }

                    var detections = ocr.DetectPlates(tile);

                    foreach (var box in detections)
                    {
                        // 1. Box is relative to tile (640x640)

                        // 2. Move box to full detectionImage coordinates
                        int absX1 = x + box.Left;
                        int absY1 = y + box.Top;
                        int absX2 = x + box.Right;
                        int absY2 = y + box.Bottom;

                        // 3. Scale to original full-resolution image
                        int fullX1 = (int)(absX1 * scaleX);
                        int fullY1 = (int)(absY1 * scaleY);
                        int fullX2 = (int)(absX2 * scaleX);
                        int fullY2 = (int)(absY2 * scaleY);

                        // 4. Add padding
                        int padX = (int)((fullX2 - fullX1) * paddingPercent);
                        int padY = (int)((fullY2 - fullY1) * paddingPercent);

                        fullX1 = Math.Max(0, fullX1 - padX);
                        fullY1 = Math.Max(0, fullY1 - padY);
                        fullX2 = Math.Min(original.Width, fullX2 + padX);
                        fullY2 = Math.Min(original.Height, fullY2 + padY);

                        var finalCropBox = new SKRectI(fullX1, fullY1, fullX2, fullY2);

                        // 5. Crop from original image
                        var cropped = ocr.Crop(original, finalCropBox);

                        if (cropped != null)
                        {
                            croppedPlates.Add(cropped);
                        }
                    }
                }
            }

            return croppedPlates;
        }

        // boxes outside with small size should be reomved
        private List<SKBitmap> CreateOverlappingTilesListFromStream(MemoryStream stream, int tileSize = 640, float overlap = 0.1f)
        {
            var original = SKBitmap.Decode(stream);
            var tiles = new List<SKBitmap>();
            int width = original.Width;
            int height = original.Height;

            int step = (int)(tileSize * (1f - overlap));
            if (step <= 0) step = 1; // just in case

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    int w = Math.Min(tileSize, width - x);
                    int h = Math.Min(tileSize, height - y);

                    var tile = CutAndPadTile(original, x, y, w, h, tileSize);

                    if (ocr.DetectPlates(tile).Any())
                        continue; // skip if no plates detected

                    tiles.Add(tile);
                }
            }

            return tiles;
        }

        private SKBitmap CutAndPadTile(SKBitmap original, int startX, int startY, int width, int height, int targetSize)
        {
            var paddedTile = new SKBitmap(targetSize, targetSize);
            using (var canvas = new SKCanvas(paddedTile))
            {
                canvas.Clear(SKColors.Black); // fill padding with black

                var srcRect = new SKRectI(startX, startY, startX + width, startY + height);
                var dstRect = new SKRect(0, 0, width, height); // top-left of canvas

                canvas.DrawBitmap(original, srcRect, dstRect);
            }

            return paddedTile;
        }

    }
}

