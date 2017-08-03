using System;
using Windows.Media;

namespace cameraFaceIdSample.Classes
{
    class PreviewFrameProcessedEventArgs<T> : EventArgs
    {
        public PreviewFrameProcessedEventArgs()
        {
        }
        public PreviewFrameProcessedEventArgs(
          T processingResults,
          VideoFrame frame)
        {
            Results = processingResults;
            Frame = frame;
        }
        public T Results { get; set; }
        public VideoFrame Frame { get; set; }
    }
}