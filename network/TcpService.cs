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
        /*  TODO:: add waitClose    */
        public class TcpService
        {
            private Dictionary<int, Func<Session, byte[], Task>> mHandlers = new Dictionary<int, Func<Session, byte[], Task>>();
            private Dictionary<Int64, Session> mSessions = new Dictionary<Int64, Session>();
            private ReaderWriterLockSlim mSessionsLock = new ReaderWriterLockSlim();
            private Int32 mNextID = 0;

            public async Task processPacket(Session session, byte[] netMsgData, int msgID)
            {
                await mHandlers[msgID](session, netMsgData);
            }

            /*  TODO:: msgid    */
            public void register<T>(Func<Session, T, Task> callback, int msgID) where T : Google.Protobuf.IMessage<T>
            {
                var descriptorProperty = typeof(T).GetProperty("Descriptor");
                MessageDescriptor descriptor = descriptorProperty.GetValue(null) as MessageDescriptor;
                mHandlers.Add(msgID, async (Session session, byte[] netMsgData) =>
                {
                    IMessage message = descriptor.Parser.ParseFrom(netMsgData);
                    if(message is T)
                    {
                        await callback(session, (T)message);
                    }
                    else
                    {
                        System.Console.WriteLine("pb message error, {0} -> {1}", message.Descriptor.Name, typeof(T).Name);
                    }
                });
            }

            public Session findSession(Int64 id)
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

            public void startConnector(string ip, int port, Action<Session> enterCallback, Action<Session> disconnectCallback)
            {
                Task.Run(async () =>
                {
                    System.Console.WriteLine("connect {0}:{1}", ip, port);
                    TcpClient c = new TcpClient();
                    await c.ConnectAsync(IPAddress.Parse(ip), port);
                    newSessionTask(c, enterCallback, disconnectCallback);
                });
            }

            public void startListen(string ip, int port, Action<Session> enterCallback, Action<Session> disconnectCallback)
            {
                Task.Run(async () =>
                {
                    System.Console.WriteLine("listen {0}:{1}", ip, port);
                    var server = new TcpListener(IPAddress.Parse(ip), port);
                    server.Start();
                    while (true)
                    {
                        newSessionTask(await server.AcceptTcpClientAsync(), enterCallback, disconnectCallback);
                    }
                });
            }

            public static Int32 DateTimeToUnixTimestamp(DateTime dateTime)
            {
                var start = new DateTime(1970, 1, 1, 0, 0, 0, dateTime.Kind);
                return Convert.ToInt32((dateTime - start).TotalSeconds);
            }

            private void newSessionTask(TcpClient client, Action<Session> enterCallback, Action<Session> disconnectCallback)
            {
                Session session = null;
                mSessionsLock.EnterWriteLock();
                mNextID++;
                Int64 id = (Int64)mNextID << 32;
                id |= (Int64)DateTimeToUnixTimestamp(DateTime.Now);
                session = new Session(this, client, id);
                mSessions[id] = session;
                mSessionsLock.ExitWriteLock();

                session.DisCallback = disconnectCallback;
                session.run();

                if (enterCallback != null)
                {
                    enterCallback(session);
                }
            }
        }
    }
}
