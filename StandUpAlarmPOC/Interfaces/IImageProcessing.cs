using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StandUpAlarmPOC.Interfaces
{
    public interface IImageProcessing
    {
        Task<(ImageSource image, string text)> ProcessUploadedImage(Stream image);
    }
}
