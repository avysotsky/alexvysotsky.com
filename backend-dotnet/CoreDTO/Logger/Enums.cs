using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace CoreDTO.Logger
{
    public class Enums
    {
        public delegate void LogAction(string s, LogLevel l);
        public enum LogLevel
        {
            llSilent,
            llExceptions,
            llInit,
            llBaselogic,
            llExtLogic,
            llFull
        }

        public static LogLevel ParseLogLevelFromString(string s)
        {
            return (LogLevel) Enum.Parse(typeof(LogLevel), s);
        }
    }

    public class StringLogger : AbstractStringLogger<string>
    {
        public StringLogger(string fileName) : base(fileName) { }

        public override void OnNext(string line)
        {
            WriteLine(line);
        }
    }

    public abstract class AbstractStringLogger<T> : IObserver<T>, IStartStopThread
    {
        protected Thread thread;
        protected AutoResetEvent waitData = new AutoResetEvent(false);
        protected ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        protected bool is_running = false;

        protected readonly string fileName;
        private StreamWriter writer;
        public AbstractStringLogger(string fileName)
        {
            this.fileName = fileName;
            writer = new StreamWriter(fileName, true);
            thread = new Thread(MainLoop);
            thread.Priority = ThreadPriority.Lowest;
        }

        protected void MainLoop()
        {
            while (is_running)
            {
                waitData.WaitOne();
                while (queue.TryDequeue(out var line))
                {
                    try
                    {
                        writer.WriteLine(line);
                        writer.Flush();
                    }
                    catch
                    {

                    }
                }
            }
            writer.Close();
        }

        public void Start()
        {
            is_running = true;
            thread.Start();
        }

        public void Stop()
        {
            is_running = false;
            waitData.Set();
        }

        public void OnCompleted()
        {
            writer.Close();
        }

        public void OnError(Exception error)
        {
            throw error;
        }

        protected void WriteLine(string line)
        {
            queue.Enqueue($"{DateTime.UtcNow:yyyy.MM.dd HH:mm:ss.fff};{line}");
            waitData.Set();
        }

        public abstract void OnNext(T value);

    }

    public interface IStartStopThread
    {
        void Start();
        void Stop();
    }
}
