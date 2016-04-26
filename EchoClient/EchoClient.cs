using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.Examples.AddressBook;
using dodo.net;

namespace TestServer
{
    class example
    {
        static int count = 0;

        private static async Task handleAddersBook(Session session, AddressBook ab)
        {
            Interlocked.Increment(ref count);
            await session.sendProtobuf(ab);  /*  echo    */
        }

        private static void Main(string[] args)
        {
            var service = new TcpService();

            service.register<AddressBook>(handleAddersBook);
            for (int i = 0; i < 5000; i++)
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

                    await session.sendProtobuf(ab);
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
