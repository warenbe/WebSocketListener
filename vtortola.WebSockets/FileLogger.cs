/*
	Copyright (c) 2017 Denis Zykov
	License: https://opensource.org/licenses/MIT
*/
using System;
using System.IO;
using System.Text;

namespace vtortola.WebSockets
{
    public sealed class FileLogger : ILogger
    {
        private FileStream fileStream;

        public static FileLogger Instance = new FileLogger();

        /// <inheritdoc />
        public bool IsDebugEnabled { get; set; }
        /// <inheritdoc />
        public bool IsWarningEnabled { get; set; }
        /// <inheritdoc />
        public bool IsErrorEnabled { get; set; }

        public FileLogger()
        {
            this.IsDebugEnabled = true;
            this.IsWarningEnabled = true;
            this.IsErrorEnabled = true;
            fileStream = new FileStream("WebsocketListener.log", FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        }

        /// <inheritdoc />
        public void Debug(string message, Exception error = null)
        {
            if (this.IsDebugEnabled == false)
                return;

            if (string.IsNullOrEmpty(message) == false)
            {
                byte[] buffOut = Encoding.UTF8.GetBytes(message);
                fileStream.Write(buffOut, 0, buffOut.Length);
            }

            if (error != null)
            {
                byte[] buffOut = Encoding.UTF8.GetBytes(error.Message);
                fileStream.Write(buffOut, 0, buffOut.Length);
            }
        }
        /// <inheritdoc />
        public void Warning(string message, Exception error = null)
        {
            if (this.IsWarningEnabled == false)
                return;

            if (string.IsNullOrEmpty(message) == false)
            {
                byte[] buffOut = Encoding.UTF8.GetBytes("[WARN] " + message);
                fileStream.Write(buffOut, 0, buffOut.Length);
            }

            if (error != null)
            {
                byte[] buffOut = Encoding.UTF8.GetBytes(error.Message);
                fileStream.Write(buffOut, 0, buffOut.Length);
            }
        }
        /// <inheritdoc />
        public void Error(string message, Exception error = null)
        {
            if (this.IsErrorEnabled == false)
                return;

            if (string.IsNullOrEmpty(message) == false)
            {
                byte[] buffOut = Encoding.UTF8.GetBytes("[ERROR] " +message);
                fileStream.Write(buffOut, 0, buffOut.Length);
            }

            if (error != null)
            {
                byte[] buffOut = Encoding.UTF8.GetBytes(error.Message);
                fileStream.Write(buffOut, 0, buffOut.Length);
            }
        }
    }
}
