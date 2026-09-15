using CoreDTO.Logger;
using System.Collections.Concurrent;
using System.Threading;

namespace CoreDTO.Threads
{
    public abstract class ThreadAdapter<T> : IStartStopThread
    {
        protected Thread thread;
        protected AutoResetEvent waitData = new AutoResetEvent(false);
        protected ConcurrentQueue<T> queue = new ConcurrentQueue<T>();
        protected bool is_running = false;

        public ThreadAdapter()
        {
            thread = new Thread(MainLoop)
            {
                Priority = ThreadPriority.Lowest
            };
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

        protected abstract void ProcessNextItem(T item);
        protected abstract void Close();
        protected abstract void Open();

        private void MainLoop()
        {
            Open();
            while (is_running)
            {
                waitData.WaitOne();
                while (queue.TryDequeue(out var item))
                {
                    try
                    {
                        ProcessNextItem(item);
                    }
                    catch
                    {

                    }
                }
            }
            Close();
        }

        public void AddItem(T item)
        {
            queue.Enqueue(item);
            waitData.Set();
        }
    }
}
