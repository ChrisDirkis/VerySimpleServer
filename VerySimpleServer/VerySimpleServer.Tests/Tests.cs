using System;
using System.Threading.Tasks;
using Xunit;

namespace VerySimpleServer.Tests {
    public class Tests {
        [Fact]
        public async void BasicServerBuildsAndRunsSuccessfully() {
            using (var server = new VerySimpleServer.Builder()
                .WithLocalhost()
                .WithGetRoute("/", "Hello World!")
                .Build()) {
                server.Start();
                server.Stop();
            }
        }
    }
}
