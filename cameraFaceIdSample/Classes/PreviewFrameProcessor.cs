﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;

namespace cameraFaceIdSample.Classes
{
    abstract class PreviewFrameProcessor<T>
    {
        public event EventHandler<PreviewFrameProcessedEventArgs<T>> FrameProcessed;
        public PreviewFrameProcessor(MediaCapture mediaCapture,
          VideoEncodingProperties videoEncodingProperties)
        {
            this.mediaCapture = mediaCapture;
            videoSize = new Rect(0, 0, videoEncodingProperties.Width,
              videoEncodingProperties.Height);
            eventArgs = new PreviewFrameProcessedEventArgs<T>();
        }
        public async Task RunFrameProcessingLoopAsync(CancellationToken token)
        {
            await Task.Run(async () =>
            {
                await this.InitialiseForProcessingLoopAsync();
                VideoFrame frame = new VideoFrame(this.BitmapFormat,
                  (int)this.videoSize.Width, (int)this.videoSize.Height);
                TimeSpan? lastFrameTime = null;
                try
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        await this.mediaCapture.GetPreviewFrameAsync(frame);
                        if ((!lastFrameTime.HasValue) ||
                          (lastFrameTime != frame.RelativeTime))
                        {
                            T results = await this.ProcessBitmapAsync(frame.SoftwareBitmap);
                            this.eventArgs.Frame = frame;
                            this.eventArgs.Results = results;
                            this.FireFrameProcessedEvent();
                        }
                        lastFrameTime = frame.RelativeTime;
                    }
                }
                finally
                {
                    frame.Dispose();
                }
            },
            token);
        }
        protected abstract Task InitialiseForProcessingLoopAsync();
        protected abstract Task<T> ProcessBitmapAsync(SoftwareBitmap bitmap);
        protected abstract BitmapPixelFormat BitmapFormat
        {
            get;
        }
        void FireFrameProcessedEvent()
        {
            var handlers = this.FrameProcessed;

            if (handlers != null)
            {
                handlers(this, this.eventArgs);
            }
        }
        PreviewFrameProcessedEventArgs<T> eventArgs;
        MediaCapture mediaCapture;
        Rect videoSize;
    }
}