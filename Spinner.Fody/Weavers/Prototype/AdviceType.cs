namespace Spinner.Fody.Weavers.Prototype
{
    internal enum AdviceType
    {
        MethodEntry,
        MethodExit,
        MethodSuccess,
        MethodException,
        MethodYield,
        MethodResume,

        MethodInvoke,

        LocationGetValue,
        LocationSetValue,

        EventAddHandler,
        EventRemoveHandler,
        EventInvokeHandler
    }
}