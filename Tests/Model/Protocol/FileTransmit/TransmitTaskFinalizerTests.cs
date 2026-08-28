using System;
using System.Reflection;
using _1RM.Model.Protocol.FileTransmit.Transmitters.TransmissionController;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Model.Protocol.FileTransmit
{
    /// <summary>
    /// TransmitTask used to have a finaliser that called TryCancel(), and TryCancel raises PropertyChanged
    /// and invokes OnTaskEnd. Both of those run the transfer pane's code: a binding update off the
    /// dispatcher throws, and an exception leaving a finaliser takes the process down with no dialog and no
    /// log line. Nothing was gained for the risk either — while a transfer runs, the Task.Run in
    /// StartTransmitAsync keeps the object alive, so a finaliser can only run once there is nothing to
    /// cancel.
    ///
    /// This is a shape assertion rather than a behaviour one, because the behaviour is a garbage collection
    /// nobody can schedule. It reads the same on Windows and on Linux.
    /// </summary>
    [TestClass]
    public class TransmitTaskFinalizerTests
    {
        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
        }

        [TestMethod]
        public void TransmitTaskDoesNotRunTransferPaneCodeOnTheFinaliserThread()
        {
            var finalize = typeof(TransmitTask).GetMethod("Finalize",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(finalize, "every type inherits object.Finalize; finding none means this test is looking in the wrong place");
            Assert.AreEqual(typeof(object), finalize!.DeclaringType,
                "TransmitTask declares a finaliser again. TryCancel raises PropertyChanged and invokes OnTaskEnd, "
                + "and an exception out of either one on the finaliser thread ends the process silently.");
        }
    }
}
