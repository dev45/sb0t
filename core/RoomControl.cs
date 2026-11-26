using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using core.Extensions;
using iconnect;

namespace core
{
    /// <summary>Puente publico para controlar la sala desde fuera (API de administracion).</summary>
    public static class RoomControl
    {
        public sealed class Result<T>
        {
            public bool Completed;
            public T Value;
            public string Error;
        }

        private static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private static readonly object hostLock = new object();
        private static ExHost host;

        private static ExHost Host
        {
            get
            {
                lock (hostLock)
                {
                    if (host == null)
                        host = new ExHost(String.Empty);

                    return host;
                }
            }
        }

        public static IPool Users { get { return Host.Users; } }
        public static IRoom Room { get { return Host.Room; } }
        public static IStats Stats { get { return Host.Stats; } }

        /// <summary>Se llama desde el hilo del servidor en cada ciclo.</summary>
        internal static void Drain()
        {
            Action work;
            int count = 0;

            while (count++ < 50 && queue.TryDequeue(out work))
            {
                try { work(); }
                catch { }
            }
        }

        /// <summary>Ejecuta la orden en el hilo del servidor. Si no se atiende a tiempo, se descarta.</summary>
        public static async Task<Result<T>> RunAsync<T>(Func<T> work, int timeoutMs = 5000)
        {
            if (!Settings.RUNNING)
                return new Result<T> { Error = "La sala no esta en marcha" };

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            int state = 0; // 0 = en cola, 1 = ejecutandose, 2 = abandonada por tiempo

            queue.Enqueue(() =>
            {
                if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
                    return;

                try { tcs.SetResult(work()); }
                catch (Exception e) { tcs.SetException(e); }
            });

            Task winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));

            if (winner != tcs.Task && Interlocked.CompareExchange(ref state, 2, 0) == 0)
                return new Result<T> { Error = "La sala no respondio a tiempo" };

            try
            {
                return new Result<T> { Completed = true, Value = await tcs.Task };
            }
            catch (Exception e)
            {
                return new Result<T> { Completed = true, Error = e.Message };
            }
        }
    }
}