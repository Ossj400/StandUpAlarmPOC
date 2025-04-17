using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StandUpAlarmPOC.Interfaces
{
    public interface ICamera
    {
        Task StartPreviewAsync();
        Task<Stream> CaptureFrameAsync();
        Task StopPreviewAsync();
        Task InitializeCameraAsync();
        Task EnsureCaptureSessionAsync();
        Task OpenCameraAsync();
        Task CloseCameraAsync();


    }
}
