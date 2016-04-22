using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.Examples.AddressBook;
using dodo.net;

namespace example
{
    class Program
    {
        static int count = 0;   /*  todo::thread safe  */

        private static async Task handleAddersBook(Session session, AddressBook ab)
        {
            count++;
            /*  echo    */
            await session.sendProtobuf(ab, 1);
        }

        private static void Main(string[] args)
        {
            var service = new TcpService();
            /*  注册消息处理函数    */
            service.register<AddressBook>(handleAddersBook, 1);
            /*  开始监听服务  */
            service.startListen("127.0.0.1", 20000, null);

            while (true)
            {
                Thread.Sleep(1000);
                System.Console.WriteLine("count : {0}", count);
                count = 0;
            }
        }
    }
}
