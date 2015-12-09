using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PixyWatch_fake
{
    class Program
    {
        static void Main(string[] args)
        {
            while (true)
            {
                System.Threading.Thread.Sleep(1000);                
                Console.WriteLine(
                    "{\"Msg\": \"Fake Stuff\" }"
                    );
            }
        }
    }
}
