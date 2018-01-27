using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Net;

namespace FYJ.Download
{
    class ThreadDownloadInfo
    {
        public long startLength; //开始位置
        public int length; //下载量
    }

    public class DownloadHelper
    {
        private const int DOWNLOAD_BUFFER_SIZE = 102400; //每次下载量 100KB
        private const int THREAD_BUFFER_SIZE = 10485760; //每个线程每次最大下载大小  设为10MB  不能太小 否则会创建太多的request对象
        public delegate void ErrorMakedEventHandler(String errorString);
        public event ErrorMakedEventHandler ErrorMakedEvent;
        public delegate void DownloadEventHandler(DownloadEvent e);
        public event DownloadEventHandler DownloadEvent;
        public delegate void StopEventHandler();
        public event StopEventHandler StopEvent;
        private object locker = new object();
        private long downloadSize = 0; //已经下载的字节
        private CancellationTokenSource cancelTokenSource = new CancellationTokenSource();
        private ManualResetEvent mre = new ManualResetEvent(true); //初始化不等待
        private AutoResetEvent eventFinished = new AutoResetEvent(false);

        private void ThreadWork(string url, FileStream fs, Queue<ThreadDownloadInfo> downQueue)
        {
            mre.WaitOne();
            if (cancelTokenSource.IsCancellationRequested)
            {
                return;
            }

            ThreadDownloadInfo downInfo = null;
            Monitor.Enter(downQueue);
            if (downQueue.Count == 0)
            {
                return;
            }
            downInfo = downQueue.Dequeue();
            Monitor.Exit(downQueue);

            HttpWebRequest request = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(url);
            request.AddRange(downInfo.startLength); //设置Range值
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            System.IO.Stream ns = response.GetResponseStream();
            byte[] nbytes = new byte[DOWNLOAD_BUFFER_SIZE];
            int temp = 0;
            int nReadSize = 0;
            byte[] buffer = new byte[downInfo.length]; //文件写入缓冲
            nReadSize = ns.Read(nbytes, 0, Math.Min(DOWNLOAD_BUFFER_SIZE, downInfo.length));
            while (temp < downInfo.length)
            {
                mre.WaitOne();
                Buffer.BlockCopy(nbytes, 0, buffer, temp, nReadSize);
                lock (locker)
                {
                    this.downloadSize += nReadSize;
                }
                temp += nReadSize;
                nReadSize = ns.Read(nbytes, 0, Math.Min(DOWNLOAD_BUFFER_SIZE, downInfo.length - temp));
            }

            lock (locker)
            {
                fs.Seek(downInfo.startLength, SeekOrigin.Begin);
                fs.Write(buffer, 0, buffer.Length);
            }

            ns.Close();
            ThreadWork(url, fs, downQueue);
        }

        public async void StartDownload(DownloadInfo info)
        {
            this.downloadSize = 0;
            if (String.IsNullOrEmpty(info.DownLoadUrl))
                throw new Exception("下载地址不能为空！");
            if (info.DownLoadUrl.ToLower().StartsWith("http://"))
            {
                await Task.Run(() =>
                {
                    bool isError = false;
                    try
                    {
                        long totalSize = 0;
                        long threadInitedLength = 0; //分配线程任务的下载量

                        #region 获取文件信息
                        //打开网络连接
                        System.Net.HttpWebRequest initRequest = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(info.DownLoadUrl);
                        System.Net.WebResponse initResponse = initRequest.GetResponse();
                        FileMessage fileMsg = GetFileMessage(initResponse);
                        totalSize = fileMsg.Length;
                        if ((!String.IsNullOrEmpty(fileMsg.FileName)) && info.LocalSaveFolder != null)
                        {
                            info.SavePath = Path.Combine(info.LocalSaveFolder, fileMsg.FileName);
                        }
                        //ReaderWriterLock readWriteLock = new ReaderWriterLock();
                        #endregion

                        #region 读取配置文件
                        string configPath = info.SavePath.Substring(0, info.SavePath.LastIndexOf(".")) + ".cfg";
                        List<object> initInfo = null;
                        if (File.Exists(configPath) && (info.IsNew == false))
                        {
                            initInfo = this.ReadConfig(configPath);
                            downloadSize = (long)initInfo[0];
                            totalSize = (long)initInfo[1];
                        }
                        #endregion

                        #region  计算速度
                        //Stopwatch MyStopWatch = new Stopwatch();
                        long lastDownloadSize = 0; //上次下载量
                        bool isSendCompleteEvent = false; //是否完成
                        Timer timer = new Timer(new TimerCallback((o) =>
                        {
                            if (!isSendCompleteEvent && !isError)
                            {
                                DownloadEvent e = new DownloadEvent();
                                e.DownloadSize = downloadSize;
                                e.TotalSize = totalSize;
                                if (totalSize > 0 && downloadSize == totalSize)
                                {
                                    e.Speed = 0;
                                    isSendCompleteEvent = true;
                                    eventFinished.Set();
                                }
                                else
                                {
                                    e.Speed = downloadSize - lastDownloadSize;
                                    lastDownloadSize = downloadSize; //更新上次下载量
                                }

                                DownloadEvent(e);
                            }

                        }), null, 0, 1000);
                        #endregion

                        string tempPath = info.SavePath.Substring(0, info.SavePath.LastIndexOf(".")) + ".dat";

                        #region 多线程下载
                        //分配下载队列
                        Queue<ThreadDownloadInfo> downQueue = null;
                        if (initInfo == null || info.IsNew)
                        {
                            downQueue = new Queue<ThreadDownloadInfo>(); //下载信息队列
                            while (threadInitedLength < totalSize)
                            {
                                ThreadDownloadInfo downInfo = new ThreadDownloadInfo();
                                downInfo.startLength = threadInitedLength;
                                downInfo.length = (int)Math.Min(Math.Min(THREAD_BUFFER_SIZE, totalSize - threadInitedLength), totalSize / info.ThreadCount); //下载量
                                downQueue.Enqueue(downInfo);
                                threadInitedLength += downInfo.length;
                            }
                        }
                        else
                        {
                            downQueue = (Queue<ThreadDownloadInfo>)initInfo[2];
                        }

                        System.IO.FileStream fs = new FileStream(tempPath, FileMode.OpenOrCreate);
                        fs.SetLength(totalSize);
                        int threads = info.ThreadCount;

                        for (int i = 0; i < info.ThreadCount; i++)
                        {
                            ThreadPool.QueueUserWorkItem((state) =>
                            {
                                ThreadWork(info.DownLoadUrl, fs, downQueue);
                                if (Interlocked.Decrement(ref threads) == 0)
                                {
                                    (state as AutoResetEvent).Set();
                                }
                            }, eventFinished);
                        }

                        //等待所有线程完成
                        eventFinished.WaitOne();
                        if (fs != null)
                        {
                            fs.Close();
                        }
                        fs = null;
                        if (File.Exists(info.SavePath))
                        {
                            File.Delete(info.SavePath);
                        }

                        if (downloadSize == totalSize)
                        {
                            File.Move(tempPath, info.SavePath);
                            File.Delete(configPath);
                        }

                        if (cancelTokenSource.IsCancellationRequested && StopEvent != null)
                        {
                            StopEvent();
                            //保存配置文件
                            SaveConfig(configPath, downloadSize, totalSize, downQueue);
                        }
                        #endregion

                    }
                    catch (Exception ex)
                    {
                        isError = true;
                        if (ErrorMakedEvent != null)
                        {
                            ErrorMakedEvent(ex.Message);
                        }
                    }
                });
            }
        }

        public void Stop()
        {
            cancelTokenSource.Cancel();
        }

        public void Suspend()
        {
            mre.Reset();
        }

        public void Resume()
        {
            mre.Set();
        }

        #region  获取文件信息
        public class FileMessage
        {
            public long Length { get; set; }
            public string FileName { get; set; }
        }
        public FileMessage GetFileMessage(System.Net.WebResponse response)
        {
            FileMessage info = new FileMessage();

            if (response.Headers["Content-Disposition"] != null)
            {
                Match match = Regex.Match(response.Headers["Content-Disposition"], "filename=(.*)");
                if (match.Success)
                {
                    string fileName = match.Groups[1].Value;
                    Encoding encoding = Encoding.UTF8;
                    string str = (response as HttpWebResponse).CharacterSet;
                    if (!String.IsNullOrEmpty(str))
                    {
                        encoding = Encoding.GetEncoding(str);
                    }
                    info.FileName = System.Web.HttpUtility.UrlDecode(fileName, encoding);
                }
            }

            if (response.Headers["Content-Length"] != null)
            {
                info.Length = long.Parse(response.Headers.Get("Content-Length"));
            }
            else
            {
                info.Length = response.ContentLength;
            }

            return info;
        }


        #endregion

        private void SaveConfig(string configPath, long downloadSize, long totalSize, Queue<ThreadDownloadInfo> downQueue)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(downloadSize + ";" + totalSize + ";");
            foreach (ThreadDownloadInfo info in downQueue)
            {
                sb.Append("(" + info.startLength + ",");
                sb.Append(info.length + ");");
            }

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            string str = Convert.ToBase64String(buffer);
            File.WriteAllText(configPath, str);
        }

        private List<object> ReadConfig(string configPath)
        {
            List<object> list = new List<object>();
            string str = File.ReadAllText(configPath);
            byte[] buffer = Convert.FromBase64String(str);
            str = System.Text.Encoding.UTF8.GetString(buffer);
            lock (locker)
            {
                string[] split = str.Split(';');
                long downloadSize = Convert.ToInt64(split[0]);
                long totalSize = Convert.ToInt64(split[1]);
                Queue<ThreadDownloadInfo> downQueue = new Queue<ThreadDownloadInfo>(); //下载信息队列
                foreach (Match match in Regex.Matches(str, "\\((\\d+),(\\d+)\\);"))
                {
                    ThreadDownloadInfo downInfo = new ThreadDownloadInfo();
                    downInfo.startLength = Convert.ToInt64(match.Groups[1].Value);
                    downInfo.length = Convert.ToInt32(match.Groups[2].Value);
                    downQueue.Enqueue(downInfo);
                }

                list.Add(downloadSize);
                list.Add(totalSize);
                list.Add(downQueue);
            }

            return list;
        }
    }
}
