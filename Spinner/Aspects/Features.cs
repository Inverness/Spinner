using System;

namespace Spinner.Aspects
{
    /// <summary>
    ///     Describes the code generation features that can be enabled for an aspect.
    /// </summary>
    [Flags]
    public enum Features : uint
    {
        /// <summary>
        ///     No features.
        /// </summary>
        None = 0x00000000,

        /// <summary>
        ///     Enables OnEntry().
        /// </summary>
        OnEntry = 0x00000001,

        /// <summary>
        ///     Enables OnExit().
        /// </summary>
        OnExit = 0x00000002,

        /// <summary>
        ///     Enables OnSuccess().
        /// </summary>
        OnSuccess = 0x00000004,

        /// <summary>
        ///     Enables FilterException() and OnException().
        /// </summary>
        OnException = 0x00000008,

        /// <summary>
        ///     Enables OnYield().
        /// </summary>
        OnYield = 0x00000010,

        /// <summary>
        ///     Enables OnResume().
        /// </summary>
        OnResume = 0x00000020,

        /// <summary>
        ///     Enables all advice methods.
        /// </summary>
        AllAdvices = OnEntry | OnExit | OnSuccess | OnException | OnYield | OnResume,

        /// <summary>
        ///     Whether the instance will be provided with the advice args. This feature is implied if any other
        ///     features are enabled that require AdviceArgs.
        /// </summary>
        Instance = 0x00000040,

        /// <summary>
        ///     Whether arguments will be provided to the advice args.
        /// </summary>
        GetArguments = 0x00000080,

        /// <summary>
        ///     Whether arguments can be set from the advice args.
        /// </summary>
        SetArguments = 0x00000100,

        /// <summary>
        ///     Whether flow control will be allowed from an aspect by changing FlowBehavior.
        /// </summary>
        FlowControl = 0x00000200,
        
        /// <summary>
        ///     Enables getting and setting the current return value from OnSuccess().
        ///     This does not affect methods where FlowControl.Return is respected.
        /// </summary>
        ReturnValue = 0x00000400,

        /// <summary>
        ///     Whether an aspect will be allowed to get and set the current yielded value in OnYield()
        ///     and OnResume() for iterators and async methods.
        /// </summary>
        YieldValue = 0x00000800,

        /// <summary>
        ///     Whether the MemberInfo derived class will be made available for the member the aspect
        ///     was applied to.
        /// </summary>
        MemberInfo = 0x00001000,

        /// <summary>
        ///     Whether the Tag property will be used to store user data between advice calls.
        /// </summary>
        Tag = 0x00002000,

        /// <summary>
        ///     Enables all features.
        /// </summary>
        All = 0xFFFFFFFF,
    }
}
