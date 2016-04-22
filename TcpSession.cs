using System;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Net.Sockets;
using System.Threading.Tasks.Dataflow;

namespace dodo
{
    namespace net
    {
        /*  TODO::add close function    */
        class Session
        {
            public class SendMsg
            {
                private byte[] mMsgBytes;

                public SendMsg(byte[] bytes)
                {
                    mMsgBytes = bytes;
                }

                public byte[] msgData
                {
                    get { return mMsgBytes; }
                }
            }

            private TcpClient mClient;
            private BufferBlock<SendMsg> mSendList;
            private TcpService mService;

            public Session(TcpService service, TcpClient client)
            {
                mClient = client;
                mService = service;

                init();
            }

            public async Task Send(byte[] data)
            {
                var msg = new SendMsg(data);
                await mSendList.SendAsync(msg);
            }

            public async Task sendProtobuf(IMessage pbMsg, Int32 msgID)
            {
                /*  packet: [pbLen - msgID - pbData] */

                var pbBytes = pbMsg.ToByteArray();
                var msgData = new byte[sizeof(Int32) + sizeof(Int32) + pbBytes.Length];

                var pbLenBytes = BitConverter.GetBytes((Int32)pbBytes.Length);
                var msgIDBytes = BitConverter.GetBytes(msgID);

                int pos = 0;
                pbLenBytes.CopyTo(msgData, pos); pos += sizeof(Int32);
                msgIDBytes.CopyTo(msgData, pos); pos += sizeof(Int32);
                pbBytes.CopyTo(msgData, pos);

                await Send(msgData);
            }

            private void sendThread()
            {
                Task.Run(async () =>
                {
                    var writer = mClient.GetStream();
                    while (true)
                    {
                        var msg = await mSendList.ReceiveAsync();
                        var msgBytes = msg.msgData;
                        await writer.WriteAsync(msgBytes, 0, msgBytes.Length);
                        await writer.FlushAsync();
                    }
                });
            }

            private void recvThread()
            {
                Task.Run(async () =>
                {
                    var endPoint = mClient.Client.RemoteEndPoint;

                    try
                    {
                        Console.WriteLine("Enter: {0}", mClient.Client.RemoteEndPoint);

                        byte[] lenBuffer = new byte[4];
                        byte[] msgIdBuffer = new byte[4];

                        using (var stream = mClient.GetStream())
                        {
                            while (true)
                            {
                                await stream.ReadAsync(lenBuffer, 0, 4);
                                await stream.ReadAsync(msgIdBuffer, 0, 4);
                                int len = BitConverter.ToInt32(lenBuffer, 0);
                                byte[] body = new byte[len];
                                await stream.ReadAsync(body, 0, len);
                                int cmdID = BitConverter.ToInt32(msgIdBuffer, 0);

                                await mService.processPacket(this, body, cmdID);
                            }
                        }
                    }
                    finally
                    {
                        Console.WriteLine("Exit: {0}", endPoint);
                        mClient.Close();
                    }
                });
            }

            private void init()
            {
                mSendList = new BufferBlock<SendMsg>();
                sendThread();
                recvThread();
            }
        }
    }
}
