using System;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks.Dataflow;
using System.Text;
using System.Threading;

namespace dodo
{
    namespace net
    {
        /*  TODO::完善异常处理，完善主动断开，添加等待完成结束的接口   */
        public class Session
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

            private TcpClient               mClient = null;
            private EndPoint                mEndPoint = null;
            private BufferBlock<SendMsg>    mSendList = null;
            private TcpService              mService = null;
            private Action<Session>         mDisConnectCallback = null;
            private Task                    mSendTask = null;
            private Task                    mRecvTask = null;
            private long                    mID = -1;
            private CancellationTokenSource mCalcelRead = new CancellationTokenSource();

            public Session(TcpService service, TcpClient client, long id)
            {
                mClient = client;
                mService = service;
                mID = id;
                mSendList = new BufferBlock<SendMsg>();
                mEndPoint = mClient.Client.RemoteEndPoint;
            }

            public long ID
            {
                set { mID = value;  }
                get { return mID; }
            }

            public Action<Session> DisCallback
            {
                get { return mDisConnectCallback; }
                set { mDisConnectCallback = value; }
            }

            public void run()
            {
                if (mSendTask == null)
                {
                    sendThread();
                }
                if (mRecvTask == null)
                {
                    recvThread();
                }
            }

            ~Session()
            {
                mClient?.Close();
                mSendTask?.Dispose();
                mRecvTask?.Dispose();
            }

            public void wait()
            {
                if (mRecvTask != null)
                {
                    mRecvTask.Wait();
                }
                if (mSendTask != null)
                {
                    mSendTask.Wait();
                }
            }

            public void shutdown()
            {
                mClient.Client.Shutdown(SocketShutdown.Both);
            }

            public void close()
            {
                mClient.Close();
                mCalcelRead.Cancel();
                mSendList.Post(null);
            }

            public async Task Send(byte[] data)
            {
                var msg = new SendMsg(data);
                await mSendList.SendAsync(msg);
            }

            public async Task sendProtobuf(IMessage pbMsg)
            {
                /*  packet: [(int)pbLen - (int)msgTypeNamelen - byte msgTypeName[msgTypeNamelen] - byte pbData[pbLen] */

                var pbBytes = pbMsg.ToByteArray();
                var descriptor = pbMsg.Descriptor;
                var msgTypeName = descriptor.FullName;
                byte[] msgTypeNameBytes = Encoding.UTF8.GetBytes(msgTypeName);

                var msgData = new byte[sizeof(int) + sizeof(int) + msgTypeNameBytes.Length + pbBytes.Length];
                var pbLenBytes = BitConverter.GetBytes((int)pbBytes.Length);
                var msgTypeNameLenBytes = BitConverter.GetBytes(msgTypeNameBytes.Length);

                int pos = 0;
                pbLenBytes.CopyTo(msgData, pos); pos += sizeof(int);
                msgTypeNameLenBytes.CopyTo(msgData, pos); pos += sizeof(int);
                msgTypeNameBytes.CopyTo(msgData, pos); pos += msgTypeNameBytes.Length;
                pbBytes.CopyTo(msgData, pos); pos += pbBytes.Length;

              await Send(msgData);
            }

            private void sendThread()
            {
                mSendTask = Task.Run(async () =>
                {
                    try
                    {
                        var writer = mClient.GetStream();
                        while (true)
                        {
                            var msg = await mSendList.ReceiveAsync();
                            if(msg != null)
                            {
                                await writer.WriteAsync(msg.msgData, 0, msg.msgData.Length);
                                await writer.FlushAsync();
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    finally
                    {
                        onClose();
                    }
                });
            }

            private void recvThread()
            {
                mRecvTask = Task.Run(async () =>
                {
                    try
                    {
                        /*  TODO::测试是否一次性read多个数据,进行自定义解包,提高速度   */
                        byte[] headBuffer = new byte[sizeof(int) + sizeof(int)];
                        var stream = mClient.GetStream();
                        var cancelToken = mCalcelRead.Token;
                        while (true)
                        {
                            await stream.ReadAsync(headBuffer, 0, headBuffer.Length, cancelToken);
                            if(cancelToken.IsCancellationRequested)
                            {
                                break;
                            }

                            int pbLen = BitConverter.ToInt32(headBuffer, 0);
                            int nameLen = BitConverter.ToInt32(headBuffer, sizeof(int));
                            byte[] pbBody = new byte[pbLen];
                            byte[] nameBody = new byte[nameLen];
                            await stream.ReadAsync(nameBody, 0, nameLen, cancelToken);
                            if (cancelToken.IsCancellationRequested)
                            {
                                break;
                            }
                            await stream.ReadAsync(pbBody, 0, pbLen, cancelToken);
                            if (cancelToken.IsCancellationRequested)
                            {
                                break;
                            }

                            string name = Encoding.UTF8.GetString(nameBody);

                            await mService.processPacket(this, pbBody, name);
                            if (cancelToken.IsCancellationRequested)
                            {
                                break;
                            }
                        }
                    }
                    finally
                    {
                        onClose();
                    }
                });
            }

            private void    onClose()
            {
                Console.WriteLine("on close \n");
                close();
                mService.removeSession(this);   /*  可重复调用   */
            }
        }
    }
}
