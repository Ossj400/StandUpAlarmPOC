using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Media;
using StandUpAlarmPOC.Platforms.Android.Services;
using StandUpAlarmPOC.Interfaces;
using Stream = System.IO.Stream;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Content.PM;
using Android.Views;
using Exception = System.Exception;
using Android.Hardware.Camera2.Params;
using Android.Provider;
using Application = Android.App.Application;
using Size = Android.Util.Size;
using Rect = Android.Graphics.Rect;

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

            try
            {
                var cameraIds = _cameraManager.GetCameraIdList();

               // await EnsureCaptureSessionAsync();
                var captureTcs = new TaskCompletionSource<bool>();
                var imageTcs = new TaskCompletionSource<Stream>();

                var characteristics = _cameraManager.GetCameraCharacteristics(_cameraDevice.Id);
                var sensorOrientation = characteristics.Get(CameraCharacteristics.SensorOrientation).JavaCast<Java.Lang.Integer>().IntValue();
                var lensFacing = characteristics.Get(CameraCharacteristics.LensFacing).JavaCast<Java.Lang.Integer>().IntValue();
                bool isFrontFacing = lensFacing == (int)LensFacing.Front;

                var rotation = Platform.CurrentActivity.WindowManager.DefaultDisplay.Rotation;
                int jpegOrientation;
                if (isFrontFacing)
                {
                    jpegOrientation = (sensorOrientation + rotationDegrees(rotation)) % 360;
                    jpegOrientation = (360 - jpegOrientation) % 360; // compensate the mirror
                }
                else
                {
                    jpegOrientation = (sensorOrientation - rotationDegrees(rotation) + 360) % 360;
                }
                int forcedOrientation = (sensorOrientation + 0) % 360;



                var captureRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
                captureRequestBuilder.AddTarget(_imageReader.Surface);
                captureRequestBuilder.Set(CaptureRequest.ControlMode, (int)ControlMode.Auto);
                captureRequestBuilder.Set(CaptureRequest.JpegOrientation, jpegOrientation);
                captureRequestBuilder.Set(CaptureRequest.NoiseReductionMode, 2);
                captureRequestBuilder.Set(CaptureRequest.LensOpticalStabilizationMode, 1);
                captureRequestBuilder.Set(CaptureRequest.EdgeMode,1);
                captureRequestBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.Off);
                captureRequestBuilder.Set(CaptureRequest.LensFocusDistance, 0.0f);
                ApplyZoom(captureRequestBuilder, 1f);
                var captureRequest = captureRequestBuilder.Build();

                using var captureCallback = new CameraCaptureCallback();
                using var imageListener = new ImageAvailableListener();

                captureCallback.CaptureCompleted += (s, e) => captureTcs.TrySetResult(true);
                imageListener.ImageAvailable += (s, stream) => imageTcs.TrySetResult(stream);

           //     captureRequestBuilder.Set(CaptureRequest.LensFocusDistance, 0.0f); // 0 = infinity
                _imageReader.SetOnImageAvailableListener(imageListener, _backgroundHandler);

                _captureSession.Capture(captureRequest, captureCallback, _backgroundHandler);

                await captureTcs.Task;
                return await imageTcs.Task;
            }
            catch (Exception ex)
            {
                await CloseCameraAsync();
                throw new Exception("Capture failed", ex);
            }
        }
        public void ApplyZoom(CaptureRequest.Builder builder, float zoomLevel)
        {
            var characteristics = _cameraManager.GetCameraCharacteristics(_cameraDevice.Id);

            // Get the sensor active array size (full sensor area)
            var sensorRect = (Rect)characteristics.Get(CameraCharacteristics.SensorInfoActiveArraySize);
            var maxZoom = characteristics.Get(CameraCharacteristics.ScalerAvailableMaxDigitalZoom).JavaCast<Java.Lang.Float>().FloatValue();
            zoomLevel = System.Math.Max(1f, System.Math.Min(zoomLevel, maxZoom));

            int centerX = sensorRect.CenterX();
            int centerY = sensorRect.CenterY();
            int deltaX = (int)(sensorRect.Width() / (2 * zoomLevel));
            int deltaY = (int)(sensorRect.Height() / (2 * zoomLevel));

            var zoomRect = new Rect(
                centerX - deltaX,
                centerY - deltaY,
                centerX + deltaX,
                centerY + deltaY
            );
            builder.Set(CaptureRequest.ScalerCropRegion, zoomRect);
        }
 
        private int rotationDegrees(SurfaceOrientation rotation)
        {
            return rotation switch
            {
                SurfaceOrientation.Rotation0 => 0,
                SurfaceOrientation.Rotation90 => 90,
                SurfaceOrientation.Rotation180 => 180,
                SurfaceOrientation.Rotation270 => 270,
                _ => 0,
            };
        }

        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            _cameraDevice = null;
            Console.WriteLine("Camera disconnected");
        }

        public override void OnError(CameraDevice camera, [GeneratedEnum] CameraError error)
        {
            Console.WriteLine("Error :"+error);
        }
        public async Task EnsureCaptureSessionAsync()
        {
            if (_captureSession != null)
                return;

            await _sessionLock.WaitAsync();
            try
            {
                var characteristics = _cameraManager.GetCameraCharacteristics(_cameraDevice.Id);
                var streamConfigMap = (StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap);
                Size[] jpegSizes = streamConfigMap.GetOutputSizes((int)ImageFormatType.Jpeg);
                Size maxSize = jpegSizes.OrderByDescending(s => s.Width * s.Height).First();
                _imageReader = ImageReader.NewInstance(maxSize.Width, maxSize.Height, ImageFormatType.Jpeg, 1);
                var surfaces = new List<Surface> { _imageReader.Surface };

                var tcs = new TaskCompletionSource<bool>();
                var sessionCallback = new CaptureSessionCallback(tcs, session =>
                {
                    _captureSession = session;
                });

                _cameraDevice.CreateCaptureSession(surfaces, sessionCallback, _backgroundHandler);

                await tcs.Task;
            }
            finally
            {
                _sessionLock.Release();
            }
        }




        #region Helper classes for image capture
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
        public class CameraCaptureCallback : CameraCaptureSession.CaptureCallback
        {
            public event EventHandler<CaptureCompletedEventArgs> CaptureCompleted;

            public override void OnCaptureCompleted(CameraCaptureSession session, CaptureRequest request, TotalCaptureResult result)
            {
                base.OnCaptureCompleted(session, request, result);
                CaptureCompleted?.Invoke(this, new CaptureCompletedEventArgs(result));
            }
        }

        public class CaptureCompletedEventArgs : EventArgs
        {
            public TotalCaptureResult CaptureResult { get; }

            public CaptureCompletedEventArgs(TotalCaptureResult result)
            {
                CaptureResult = result;
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

                  //  SaveImageToMediaStore(bytes);

                    ImageAvailable?.Invoke(this, new MemoryStream(bytes));
                    image.Close();

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error capturing image: {ex.Message}");
                }
            }
            private void SaveImageToMediaStore(byte[] imageData)
            {
                var context = Platform.CurrentActivity ?? Application.Context;

                var filename = $"photo_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.jpg";

                var contentValues = new ContentValues();
                contentValues.Put(MediaStore.IMediaColumns.DisplayName, filename);
                contentValues.Put(MediaStore.IMediaColumns.MimeType, "image/jpeg");
                contentValues.Put(MediaStore.IMediaColumns.RelativePath, "Pictures/Camera2App");

                var uri = context.ContentResolver.Insert(MediaStore.Images.Media.ExternalContentUri, contentValues);

                if (uri == null)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to create MediaStore entry.");
                    return;
                }

                try
                {
                    using var outputStream = context.ContentResolver.OpenOutputStream(uri);
                    outputStream.Write(imageData, 0, imageData.Length);
                    outputStream.Flush();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error writing image: {ex}");
                }
            }
        }

    }
    #endregion

}