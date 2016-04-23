using System;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks.Dataflow;

namespace dodo
{
    namespace net
    {
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
            private Int64                   mID = -1;

            public Session(TcpService service, TcpClient client, Int64 id)
            {
                mClient = client;
                mService = service;
                mID = id;
                mSendList = new BufferBlock<SendMsg>();
                mEndPoint = mClient.Client.RemoteEndPoint;
            }

            public Int64 ID
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
                mClient.Close();
            }

            public void wait()
            {
                if(mSendTask != null)
                {
                    mSendTask.Wait();
                }
                if(mRecvTask != null)
                {
                    mRecvTask.Wait();
                }
            }

            public void shutdown()
            {
                try
                {
                    mClient.Client.Shutdown(SocketShutdown.Both);
                }
                finally
                { }
            }

            public void close()
            {
                mClient.Close();
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
                mSendTask = Task.Run(async () =>
                {
                    try
                    {
                        var writer = mClient.GetStream();
                        while (true)
                        {
                            var msg = await mSendList.ReceiveAsync();
                            await writer.WriteAsync(msg.msgData, 0, msg.msgData.Length);
                            await writer.FlushAsync();
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
                        byte[] headBuffer = new byte[sizeof(Int32)+ sizeof(Int32)];
                        var stream = mClient.GetStream();
                        while (true)
                        {
                            await stream.ReadAsync(headBuffer, 0, headBuffer.Length);
                            int len = BitConverter.ToInt32(headBuffer, 0);
                            byte[] body = new byte[len];
                            await stream.ReadAsync(body, 0, len);
                            int cmdID = BitConverter.ToInt32(headBuffer, sizeof(Int32));

                            await mService.processPacket(this, body, cmdID);
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
                mClient.Close();
                mService.removeSession(this);
            }
        }
    }
}
