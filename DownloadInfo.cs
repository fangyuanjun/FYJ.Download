using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FYJ.Download
{
    public class DownloadInfo
    {
        /// <summary>
        /// 下载地址 
        /// </summary>
        public String DownLoadUrl
        {
            get;
            set;
        }

        private string _localSaveFolder;
        /// <summary>
        /// 本地保存路径
        /// </summary>
        public String LocalSaveFolder
        {
            get { return _localSaveFolder; }
            set { _localSaveFolder = value; }
        }

        private string _savePath;
        /// <summary>
        /// 包含文件名的完整保存路径
        /// </summary>
        public string SavePath
        {
            get
            {
                if (_savePath == null)
                {
                    if (_localSaveFolder == null)
                    {
                        throw new Exception("本地保存路径不能为空");
                    }

                    _savePath = Path.Combine(_localSaveFolder, Path.GetFileName(DownLoadUrl));

                    if (File.Exists(_savePath))
                    {
                        if (IsNew)
                        {
                            if (IsOver)
                            {
                                File.Delete(_savePath);
                            }
                            else
                            {
                                _savePath = _savePath.Substring(0, _savePath.LastIndexOf(".")) + "(2)" + _savePath.Substring(_savePath.LastIndexOf("."));
                            }
                        }
                    }
                }

                return _savePath;
            }
            set
            {
                _savePath = value;
            }
        }

        private int _threadCount = 1;
        /// <summary>
        /// 线程数
        /// </summary>
        public int ThreadCount
        {
            get { return _threadCount; }
            set { _threadCount = value; }
        }

        /// <summary>
        /// 是否覆盖已存在的文件
        /// </summary>
        public bool IsOver
        {
            get;
            set;
        }

        /// <summary>
        /// 是否重新下载
        /// </summary>
        public bool IsNew
        {
            get;
            set;
        }
    }
}
