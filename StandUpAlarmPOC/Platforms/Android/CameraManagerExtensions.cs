using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StandUpAlarmPOC.Platforms.Android
{
    using System.Threading.Tasks;
    using global::Android.Hardware.Camera2;
    using global::Android.OS;
    using Java.Lang;

    public static class CameraManagerExtensions
    {
        public static Task<CameraDevice> OpenCameraAsync(
            this CameraManager manager,
            string cameraId,
            CameraDevice.StateCallback callback,
            Handler handler = null)
        {
            var tcs = new TaskCompletionSource<CameraDevice>();

            var stateCallback = new CameraStateCallback(tcs, callback);

            manager.OpenCamera(cameraId, stateCallback, handler);

            return tcs.Task;
        }

        private class CameraStateCallback : CameraDevice.StateCallback
        {
            private readonly TaskCompletionSource<CameraDevice> _tcs;
            private readonly CameraDevice.StateCallback _originalCallback;

            public CameraStateCallback(
                TaskCompletionSource<CameraDevice> tcs,
                CameraDevice.StateCallback originalCallback)
            {
                _tcs = tcs;
                _originalCallback = originalCallback;
            }

            public override void OnOpened(CameraDevice camera)
            {
                _originalCallback?.OnOpened(camera);
                _tcs.TrySetResult(camera);
            }

            public override void OnDisconnected(CameraDevice camera)
            {
                _originalCallback?.OnDisconnected(camera);
                _tcs.TrySetException(new Exception("Camera disconnected"));
            }

            public override void OnError(CameraDevice camera, CameraError error)
            {
                _originalCallback?.OnError(camera, error);
                _tcs.TrySetException(new Exception($"Camera error: {error}"));
            }
        }
    }
}
