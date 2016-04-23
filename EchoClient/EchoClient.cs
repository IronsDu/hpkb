using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.Examples.AddressBook;
using dodo.net;

namespace TestServer
{
    class example
    {
        static int count = 0;   /*  todo::thread safe  */

        private static async Task handleAddersBook(Session session, AddressBook ab)
        {
            await session.sendProtobuf(ab, 1);  /*  echo    */
        }

        private static void Main(string[] args)
        {
            var service = new TcpService();

            service.register<AddressBook>(handleAddersBook, 1);
            for (int i = 0; i < 1000; i++)
            {
                service.startConnector("127.0.0.1", 20000, async (Session session) =>
                {
                    System.Console.WriteLine("onenter:{0}", i);
                    /*  send request    */
                    AddressBook ab = new AddressBook();
                    Person p = new Person();
                    p.Id = 1;
                    p.Name = "dodo";
                    ab.People.Add(p);

                    await session.sendProtobuf(ab, 1);
                }, null);
            }

            while (true)
            {
                Thread.Sleep(1000);
                System.Console.WriteLine("count : {0}", count);
                count = 0;
            }
        }
    }
}
