using System;
using System.Threading;
using Xunit;
using AndroidSyncControl.UI;

namespace AndroidSyncControl.Tests
{
    public class MainWindowTests
    {
        [Fact]
        public void MainWindow_CanInstantiateOnStaThread()
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // Ensure Application resources are available if needed
                    if (System.Windows.Application.Current == null)
                    {
                        new System.Windows.Application();
                    }

                    var window = new MainWindow();
                    Assert.NotNull(window);
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception != null)
            {
                throw new Exception($"MainWindow failed to instantiate: {exception}", exception);
            }
        }
    }
}
