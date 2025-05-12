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
using Java.Lang;
using Exception = System.Exception;
using Android.Hardware.Camera2.Params;
using System.Diagnostics;
using Android.Provider;
using Application = Android.App.Application;
using Size = Android.Util.Size;
using Bumptech.Glide;
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

                foreach (var id in cameraIds)
                {
                    var chars = _cameraManager.GetCameraCharacteristics(id);
                    var facing = chars.Get(CameraCharacteristics.LensFacing).JavaCast<Java.Lang.Integer>()?.IntValue();

                    // With this corrected code:
                    var focalLengthsObject = chars.Get(CameraCharacteristics.LensInfoAvailableFocalLengths);
                    var focalLengths = focalLengthsObject is Java.Lang.Object javaObject
                        ? javaObject.ToArray<float>()
                        : null;

                    var isBackCamera = facing == (int)LensFacing.Back;

                        var capabilitiesObject = chars.Get(CameraCharacteristics.RequestAvailableCapabilities);
                        var capabilities = capabilitiesObject is Java.Lang.Object javaObjectzz
                            ? javaObjectzz.ToArray<int>()
                            : null;


                    if (capabilities != null)
                    {
                        foreach (var cap in capabilities)
                        {
                            var name = GetCapabilityName(cap);
                        }
                    }
                } 

                await EnsureCaptureSessionAsyncNew(); // make sure session + _imageReader is ready
                //var focusLocked = await LockAutoFocusAsync();
               // if (!focusLocked)
               //     throw new TimeoutException("Autofocus did not lock in time");

                // ✅ Step 2: Capture the image
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
                //captureRequestBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.Auto);
                //captureRequestBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);
                //captureRequestBuilder.Set(CaptureRequest.ControlAeLock, true);
                //captureRequestBuilder.Set(CaptureRequest.ControlAwbMode, (int)ControlAwbMode.Auto);
                captureRequestBuilder.Set(CaptureRequest.JpegOrientation, jpegOrientation);
                captureRequestBuilder.Set(CaptureRequest.NoiseReductionMode, 2);
                captureRequestBuilder.Set(CaptureRequest.LensOpticalStabilizationMode, 1);
                captureRequestBuilder.Set(CaptureRequest.EdgeMode,1);

                captureRequestBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.Off);
                captureRequestBuilder.Set(CaptureRequest.LensFocusDistance, 0.0f); // 0 = infinity

                ApplyZoomAndFocus(captureRequestBuilder, 1f);
                var captureRequest = captureRequestBuilder.Build();
                using var captureCallback = new CameraCaptureCallback();
                using var imageListener = new ImageAvailableListener();

                captureCallback.CaptureCompleted += (s, e) => captureTcs.TrySetResult(true);
                imageListener.ImageAvailable += (s, stream) => imageTcs.TrySetResult(stream);

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
        public void ApplyZoomAndFocus(CaptureRequest.Builder builder, float zoomLevel)
        {
            var characteristics = _cameraManager.GetCameraCharacteristics(_cameraDevice.Id);

            // Get the sensor active array size (full sensor area)
            var sensorRect = (Rect)characteristics.Get(CameraCharacteristics.SensorInfoActiveArraySize);

            var maxZoom = characteristics.Get(CameraCharacteristics.ScalerAvailableMaxDigitalZoom).JavaCast<Java.Lang.Float>().FloatValue();

            // Clamp zoom level to valid range
            zoomLevel = System.Math.Max(1f, System.Math.Min(zoomLevel, maxZoom));

            // Calculate zoom crop rectangle
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

            // Apply digital zoom
            builder.Set(CaptureRequest.ScalerCropRegion, zoomRect);

            // Set autofocus region inside the zoomRect
            var afRegion = new MeteringRectangle(zoomRect, MeteringRectangle.MeteringWeightMax);
            builder.Set(CaptureRequest.LensFocusDistance, 0.0f);

        }
        private string GetCapabilityName(int capability)
        {
            return capability switch
            {
                0 => "BACKWARD_COMPATIBLE",
                1 => "MANUAL_SENSOR",
                2 => "MANUAL_POST_PROCESSING",
                3 => "RAW",
                4 => "PRIVATE_REPROCESSING",
                5 => "READ_SENSOR_SETTINGS",
                6 => "BURST_CAPTURE",
                7 => "YUV_REPROCESSING",
                8 => "DEPTH_OUTPUT",
                9 => "CONSTRAINED_HIGH_SPEED_VIDEO",
                10 => "MOTION_TRACKING",
                11 => "LOGICAL_MULTI_CAMERA",
                12 => "MONOCHROME",
                13 => "SECURE_IMAGE_DATA",
                14 => "SYSTEM_CAMERA",
                15 => "ULTRA_HIGH_RESOLUTION_SENSOR",
                16 => "REMOSAIC_REPROCESSING",
                17 => "OEM_EXTENSIONS",
                _ => $"Unknown ({capability})"
            };
        }
        private async Task<bool> LockAutoFocusAsync()
        {
            try
            {


                var tcs = new TaskCompletionSource<bool>();

                var afRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                afRequestBuilder.AddTarget(_imageReader.Surface);
                afRequestBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.Auto);
                afRequestBuilder.Set(CaptureRequest.ControlAfTrigger, (int)ControlAFTrigger.Start);

                var focusCallback = new CameraCaptureCallback();
                focusCallback.CaptureCompleted += (s, e) =>
                {
                    var afStateObj = e.CaptureResult.Get(CaptureResult.ControlAfState);
                    if (afStateObj is Java.Lang.Integer afState)
                    {
                        if (afState.IntValue() == (int)ControlAFState.FocusedLocked ||
                            afState.IntValue() == (int)ControlAFState.NotFocusedLocked)
                        {
                            tcs.TrySetResult(true);
                        }
                    }
                };

                _captureSession.Capture(afRequestBuilder.Build(), focusCallback, _backgroundHandler);

                // Wait up to 1.5 seconds for focus to lock
                return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(1500));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
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
                var imageReader = ImageReader.NewInstance(4080, 3072, ImageFormatType.Jpeg, 1);
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

                if (!await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(400)))
                    throw new TimeoutException("Session creation timed out");
            }
            finally
            {
                _sessionLock.Release();
            }
        }
        public async Task EnsureCaptureSessionAsyncNew()
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

                // ✅ Create ImageReader once here
                _imageReader = ImageReader.NewInstance(maxSize.Width, maxSize.Height, ImageFormatType.Jpeg, 1);

                var surfaces = new List<Surface> { _imageReader.Surface };

                var tcs = new TaskCompletionSource<bool>();

                var sessionCallback = new CaptureSessionCallback(tcs, session =>
                {
                    _captureSession = session;
                });

                _cameraDevice.CreateCaptureSession(surfaces, sessionCallback, _backgroundHandler);

                if (!await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(1500)))
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
        internal class CameraCaptureCallback2 : CameraCaptureSession.CaptureCallback
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
        internal class HandlerExecutor : Java.Lang.Object, Java.Util.Concurrent.IExecutor
        {
            private readonly Handler _handler;

            public HandlerExecutor(Looper looper)
            {
                _handler = new Handler(looper);
            }

            public void Execute(Java.Lang.IRunnable command)
            {
                _handler.Post(command);
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

                    SaveImageToMediaStore(bytes);


                    ImageAvailable?.Invoke(this, new MemoryStream(bytes));
                   // image.Close();

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
}