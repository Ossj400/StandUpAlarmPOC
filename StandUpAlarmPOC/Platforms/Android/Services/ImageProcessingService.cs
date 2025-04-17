using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Android.Content.Res;
using StandUpAlarmPOC.Interfaces;
using static Android.Resource;

namespace StandUpAlarmPOC.Platforms.Android.Services
{
    public class ImageProcessingService : IImageProcessing
    {


        public void HandlePlates(List<string> plates)
        {
            // Do something with the result here`
            Console.WriteLine("Plates received: " + string.Join(", ", plates));
        }

        public async Task<(ImageSource image, string text)> ProcessUploadedImage(Image image)
        {
            try
            {
                // Replace with your actual path if needed

                using var stream = await FileSystem.OpenAppPackageFileAsync("plate.png");
                var ocr = new PlateOCRService("yolo-v9-t-384-license-plates-end2end.onnx","european_mobile_vit_v2_ocr.onnx");
                var (imageRet, text) = await ocr.Run("plate.png");

                return (imageRet, text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR error: {ex.Message}");
                return (null, "No plates detected");

            }
        }
    }
}
