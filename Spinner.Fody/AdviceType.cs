namespace Spinner.Fody
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