using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Net.Sockets;
using System.Net;
using Google.Protobuf.Reflection;

namespace dodo
{
    namespace net
    {
        /*  TODO:: add session manager  */

        class TcpService
        {
            private Dictionary<int, Func<Session, byte[], Task>> mHandlers = new Dictionary<int, Func<Session, byte[], Task>>();

            public async Task processPacket(Session session, byte[] netMsgData, int msgID)
            {
                await mHandlers[msgID](session, netMsgData);
            }

            public void register<T>(Func<Session, T, Task> callback, int msgID) where T : Google.Protobuf.IMessage<T>
            {
                var descriptorProperty = typeof(T).GetProperty("Descriptor");
                MessageDescriptor descriptor = descriptorProperty.GetValue(null) as MessageDescriptor;
                mHandlers.Add(msgID, async (Session session, byte[] netMsgData) =>
                {
                    IMessage message = descriptor.Parser.ParseFrom(netMsgData);
                    await callback(session, (T)message);
                });
            }

            public void startConnector(string ip, int port, Action<Session> enterCallback)
            {
                Task.Run(async () =>
                {
                    System.Console.WriteLine("connect {0}:{1}", ip, port);
                    TcpClient c = new TcpClient();
                    await c.ConnectAsync(IPAddress.Parse(ip), port);
                    newSessionTask(c, enterCallback);
                });
            }

            public void startListen(string ip, int port, Action<Session> enterCallback)
            {
                Task.Run(async () =>
                {
                    System.Console.WriteLine("listen {0}:{1}", ip, port);
                    var server = new TcpListener(IPAddress.Parse(ip), port);
                    server.Start();
                    while (true)
                    {
                        newSessionTask(await server.AcceptTcpClientAsync(), enterCallback);
                    }
                });
            }

            private void newSessionTask(TcpClient client, Action<Session> enterCallback)
            {
                var session = new Session(this, client);
                if (enterCallback != null)
                {
                    enterCallback(session);
                }
            }
        }
    }
}
