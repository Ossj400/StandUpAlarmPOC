using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Media;
using StandUpAlarmPOC.Platforms.Android.Services;
using StandUpAlarmPOC.Interfaces;
using Stream = System.IO.Stream;
using Android.App;
using Microsoft.Maui.ApplicationModel;
using Android.OS;
using Android.Runtime;
using Java.Nio;
using Android.Widget;
using Android.Content.PM;
using Android.Views;
[assembly: Dependency(typeof(AndroidCameraService))]
namespace StandUpAlarmPOC.Platforms.Android.Services
{

    [Service(ForegroundServiceType = ForegroundService.TypeDataSync)]
    public class AndroidCameraService : CameraDevice.StateCallback, ICamera
    {
        private CameraDevice _cameraDevice;
        private CameraCaptureSession _captureSession;
        private CameraManager _cameraManager;
        private Context _context;
        private ImageReader _imageReader;
        private SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);
        private HandlerThread _backgroundThread;
        private Handler _backgroundHandler;

        public AndroidCameraService()
        {
            _backgroundThread = new HandlerThread("CameraBackground");
            _backgroundThread.Start();
            _backgroundHandler = new Handler(_backgroundThread.Looper);
        }
        public async Task InitializeCameraAsync()
        {
            _context = Platform.CurrentActivity ??
                      throw new InvalidOperationException("Android context not available");

            _cameraManager = _context.GetSystemService(Context.CameraService) as CameraManager;

            var cameraId = _cameraManager.GetCameraIdList()[0];
            _cameraDevice = await _cameraManager.OpenCameraAsync(cameraId, this);
        }
        public async Task OpenCameraAsync()
        {
            _backgroundThread = new HandlerThread("CameraBackground");
            _backgroundThread.Start();
            _backgroundHandler = new Handler(_backgroundThread.Looper);

            var cameraId = _cameraManager.GetCameraIdList()[0];
            var tcs = new TaskCompletionSource<CameraDevice>();

            _cameraManager.OpenCamera(cameraId, new CameraStateCallback(tcs), _backgroundHandler);
            _cameraDevice = await tcs.Task;
        }
        public async Task CloseCameraAsync()
        {
            try
            {
                _captureSession?.Close();
                _captureSession = null;

                _imageReader?.Close();
                _imageReader = null;

                _cameraDevice?.Close();
                _cameraDevice = null;

                _backgroundThread?.QuitSafely();
                _backgroundThread = null;
                _backgroundHandler = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cleanup error: {ex.Message}");
            }
        }
        private void EnsureContext()
        {
            _context ??= Platform.CurrentActivity ??
                throw new InvalidOperationException("Android context not available");

            _cameraManager ??= (CameraManager)_context.GetSystemService(Context.CameraService)!;
        }
        public async Task StartPreviewAsync()
        {
            var cameraId = _cameraManager.GetCameraIdList()[0];
            // should be async !! ??
            _cameraManager.OpenCameraAsync(cameraId, this, null);
        }

        public override void OnOpened(CameraDevice camera)
        {
            _cameraDevice = camera;
            // Setup preview surface here
        }

        public async Task<Stream> CaptureFrameAsync()
        {
            await EnsureCaptureSessionAsync();

            try
            {
                var captureRequest = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
                captureRequest.AddTarget(_imageReader.Surface);

                var captureTcs = new TaskCompletionSource<bool>();
                var imageTcs = new TaskCompletionSource<Stream>();

                using var captureCallback = new CameraCaptureCallback();
                using var imageListener = new ImageAvailableListener();

                captureCallback.CaptureCompleted += (s, e) => captureTcs.TrySetResult(true);
                imageListener.ImageAvailable += (s, stream) => imageTcs.TrySetResult(stream);

                _imageReader.SetOnImageAvailableListener(imageListener, _backgroundHandler);

                _captureSession.Capture(
                    captureRequest.Build(),
                    captureCallback,
                    _backgroundHandler
                );

                await captureTcs.Task;
                return await imageTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                await CloseCameraAsync();
                throw new Exception("Capture failed", ex);
            }
        }

        public async Task StopCameraAsync()
        {
            try
            {
                _captureSession?.Close();
                _captureSession = null;

                _imageReader?.Close();
                _imageReader = null;

                _cameraDevice?.Close();
                _cameraDevice = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cleanup error: {ex.Message}");
            }
        }

        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            _cameraDevice = null;
            Console.WriteLine("Camera disconnected");
        }
        public Task StopPreviewAsync()
        {
            throw new NotImplementedException();
        }



        public override void OnError(CameraDevice camera, [GeneratedEnum] CameraError error)
        {

        }
        public async Task EnsureCaptureSessionAsync()
        {
            if (_captureSession != null) return;

            await _sessionLock.WaitAsync();
            try
            {
                var imageReader = ImageReader.NewInstance(1920, 1080, ImageFormatType.Jpeg, 2);
                var surfaces = new List<Surface> { imageReader.Surface };
                var tcs = new TaskCompletionSource<bool>();

                _cameraDevice.CreateCaptureSession(
                    surfaces,
                    new CaptureSessionCallback(
                        tcs,
                        session =>
                        {
                            _captureSession = session;
                            _imageReader = imageReader;
                        }
                    ),
                    _backgroundHandler
                );

                if (!await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3)))
                    throw new TimeoutException("Session creation timed out");
            }
            finally
            {
                _sessionLock.Release();
            }
        }


        // Helper class for image capture
        internal class CaptureSessionCallback : CameraCaptureSession.StateCallback
        {
            private readonly TaskCompletionSource<bool> _tcs;
            private readonly Action<CameraCaptureSession> _onConfigured;

            public CaptureSessionCallback(
                TaskCompletionSource<bool> tcs,
                Action<CameraCaptureSession> onConfigured)
            {
                _tcs = tcs;
                _onConfigured = onConfigured;
            }

            public override void OnConfigured(CameraCaptureSession session)
            {
                _onConfigured(session);
                _tcs.TrySetResult(true);
            }

            public override void OnConfigureFailed(CameraCaptureSession session)
            {
                session.Close();
                _tcs.TrySetException(new Exception("Session configuration failed"));
            }
        }
        internal class CameraCaptureCallback : CameraCaptureSession.CaptureCallback
        {
            public event EventHandler CaptureCompleted;
            public event EventHandler CaptureFailed;

            public override void OnCaptureCompleted(
                CameraCaptureSession session,
                CaptureRequest request,
                TotalCaptureResult result)
            {
                CaptureCompleted?.Invoke(this, EventArgs.Empty);
            }

            public override void OnCaptureFailed(
                CameraCaptureSession session,
                CaptureRequest request,
                CaptureFailure failure)
            {
                CaptureFailed?.Invoke(this, EventArgs.Empty);
            }
        }

        internal class CameraStateCallback : CameraDevice.StateCallback
        {
            private readonly TaskCompletionSource<CameraDevice> _tcs;

            public CameraStateCallback(TaskCompletionSource<CameraDevice> tcs)
            {
                _tcs = tcs;
            }

            public override void OnOpened(CameraDevice camera)
            {
                _tcs.TrySetResult(camera);
            }

            public override void OnDisconnected(CameraDevice camera)
            {
                _tcs.TrySetException(new Exception("Camera disconnected"));
            }

            public override void OnError(CameraDevice camera, CameraError error)
            {
                _tcs.TrySetException(new Exception($"Camera error: {error}"));
            }
        }
        internal class ImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
        {
            public event EventHandler<Stream> ImageAvailable;


            public void OnImageAvailable(ImageReader reader)
            {
                try
                {
                    using var image = reader.AcquireNextImage();
                    using var buffer = image.GetPlanes()[0].Buffer;
                    var bytes = new byte[buffer.Remaining()];
                    buffer.Get(bytes);
                    ImageAvailable?.Invoke(this, new MemoryStream(bytes));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error capturing image: {ex.Message}");
                }
            }
        }
    }
}