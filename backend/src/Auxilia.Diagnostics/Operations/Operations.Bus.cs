namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Bus
    {
        /// <summary>
        /// Every incoming message (trace, log scope, one outcome log). Not a write operation itself: transactional
        /// handlers open the transaction that also records <c>ops.processed_messages</c>.
        /// </summary>
        public static readonly OperationDescriptor HandleMessage = new("Bus.HandleMessage", EventCodes.Bus.MessageHandled, isWrite: false);
    }
}
