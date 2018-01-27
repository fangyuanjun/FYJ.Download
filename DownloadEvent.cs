using System;
using System.Collections.Generic;
using System.Text;

namespace FYJ.Download
{
    public class DownloadEvent
    {
        private long _downloadSize;
        private long _totalSize;
        public DownloadEvent()
        {
        }

        /// <summary>
        /// 每秒下载的速度 B/s 
        /// </summary>
        public double Speed
        {
            get;
            set;
        }
        /// <summary>
        /// 每秒下载的速度  KB/s
        /// </summary>
        public String SpeedKb
        {
            get { return Math.Round(Speed / 1024.0, 2) + "KB/s"; }
        }

        public String PercentString
        {
            get { return (DownloadSize * 100.0 / TotalSize).ToString("F2") + "%"; }
        }
        public String DownloadMb
        {
            get { return (DownloadSize / 1048576.0).ToString("F2"); }
        }
        public String TotalMb
        {
            get { return (TotalSize / 1048576.0).ToString("F2"); }
        }

        public long DownloadSize
        {
            get;
            set;
        }

        public long TotalSize
        {
            get;
            set;
        }
    }
}
