using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Net.Sockets;
using System.Net;
using Google.Protobuf.Reflection;
using System.Threading;

namespace dodo
{
    namespace net
    {
        /*  TODO::添加关闭服务的接口 */
        public class TcpService
        {
            private Dictionary<string, Func<Session, byte[], Task>> mHandlers = new Dictionary<string, Func<Session, byte[], Task>>();
            private Dictionary<long, Session> mSessions = new Dictionary<long, Session>();
            private ReaderWriterLockSlim mSessionsLock = new ReaderWriterLockSlim();
            private int mNextID = 0;
            private List<TcpListener> mListeners = new List<TcpListener>();
            private List<Task> mListenerTasks = new List<Task>();
            private ReaderWriterLockSlim mListenersLock = new ReaderWriterLockSlim();

            public async Task processPacket(Session session, byte[] netMsgData, string msgTypeName)
            {
                Func<Session, byte[], Task> handle = null;
                if(mHandlers.TryGetValue(msgTypeName, out handle))
                {
                    await handle(session, netMsgData);
                }
            }

            public void register<T>(Func<Session, T, Task> callback) where T : Google.Protobuf.IMessage<T>
            {
                var descriptorProperty = typeof(T).GetProperty("Descriptor");
                MessageDescriptor descriptor = descriptorProperty.GetValue(null) as MessageDescriptor;
                mHandlers.Add(descriptor.FullName, async (Session session, byte[] netMsgData) =>
                {
                    IMessage message = descriptor.Parser.ParseFrom(netMsgData);
                    if(message is T)
                    {
                        await callback(session, (T)message);
                    }
                    else
                    {
                        Console.WriteLine("pb message error, {0} -> {1}", message.Descriptor.Name, typeof(T).Name);
                    }
                });
            }

            public Session findSession(long id)
            {
                Session ret = null;
                mSessionsLock.EnterReadLock();
                mSessions.TryGetValue(id, out ret);
                mSessionsLock.ExitReadLock();

                return ret;
            }

            public void removeSession(Session session)
            {
                bool success = false;
                var disConnnectCallback = session.DisCallback;

                mSessionsLock.EnterWriteLock();
                success = mSessions.Remove(session.ID);
                mSessionsLock.ExitWriteLock();

                if(success && disConnnectCallback != null)
                {
                    disConnnectCallback(session);
                }
            }

            public  void    waitCloseAll()
            {
                mSessionsLock.EnterWriteLock();
                try
                {
                    foreach (var l in mListeners)
                    {
                        l.Stop();
                    }
                    foreach (var lt in mListenerTasks)
                    {
                        lt.Wait();
                    }
                }
                finally
                { }
                mSessionsLock.ExitWriteLock();
            }

            public void startConnector(string ip, int port, Action<Session> enterCallback, Action<Session> disconnectCallback)
            {
                /*  TODO    */
                Task.Run(async () =>
                {
                    Console.WriteLine("connect {0}:{1}", ip, port);
                    TcpClient c = new TcpClient();
                    await c.ConnectAsync(IPAddress.Parse(ip), port);
                    newSessionTask(c, enterCallback, disconnectCallback);
                });
            }

            public void startListen(string ip, int port, Action<Session> enterCallback, Action<Session> disconnectCallback)
            {
                Console.WriteLine("listen {0}:{1}", ip, port);
                var server = new TcpListener(IPAddress.Parse(ip), port);
                server.Start();

                /*  添加到listen管理器，用于关闭服务时用   */
                mListenersLock.EnterWriteLock();
                mListeners.Add(server);

                mListenerTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        while (true)
                        {
                            var client = await server.AcceptTcpClientAsync();
                            if(client != null)
                            {
                                newSessionTask(client, enterCallback, disconnectCallback);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    finally
                    { }
                }));
                mListenersLock.ExitWriteLock();
            }

            public static int DateTimeToUnixTimestamp(DateTime dateTime)
            {
                var start = new DateTime(1970, 1, 1, 0, 0, 0, dateTime.Kind);
                return Convert.ToInt32((dateTime - start).TotalSeconds);
            }

            private void newSessionTask(TcpClient client, Action<Session> enterCallback, Action<Session> disconnectCallback)
            {
                Session session = null;
                mSessionsLock.EnterWriteLock();
                mNextID++;
                long id = (long)mNextID << 32;
                id |= (long)DateTimeToUnixTimestamp(DateTime.Now);
                session = new Session(this, client, id);
                mSessions[id] = session;
                mSessionsLock.ExitWriteLock();

                session.DisCallback = disconnectCallback;
                /*  开启会话的读写"线程" */
                session.run();

                if (enterCallback != null)
                {
                    enterCallback(session);
                }
            }
        }
    }
}
