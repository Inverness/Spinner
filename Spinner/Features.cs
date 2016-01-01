using System;

namespace Spinner
{
    /// <summary>
    ///     Describes the code generation features that can be enabled for an aspect.
    /// </summary>
    [Flags]
    public enum Features
    {
        /// <summary>
        ///     No features.
        /// </summary>
        None = 0x0000,

        /// <summary>
        ///     Enables OnEntry().
        /// </summary>
        OnEntry = 0x0001,

        /// <summary>
        ///     Enables OnExit().
        /// </summary>
        OnExit = 0x0002,

        /// <summary>
        ///     Enables OnSuccess().
        /// </summary>
        OnSuccess = 0x0004,

        /// <summary>
        ///     Enables FilterException() and OnException().
        /// </summary>
        OnException = 0x0008,

        /// <summary>
        ///     Enables OnYield().
        /// </summary>
        OnYield = 0x0010,

        /// <summary>
        ///     Enables OnResume().
        /// </summary>
        OnResume = 0x0020,

        /// <summary>
        ///     Enables all advice methods.
        /// </summary>
        AllAdvices = OnEntry | OnExit | OnSuccess | OnException | OnYield | OnResume,

        /// <summary>
        ///     Whether the instance will be provided with the advice args. This feature is implied if any other
        ///     features are enabled that require AdviceArgs.
        /// </summary>
        Instance = 0x0040,

        /// <summary>
        ///     Whether arguments will be provided to the advice args.
        /// </summary>
        GetArguments = 0x0080,

        /// <summary>
        ///     Whether arguments can be set from the advice args.
        /// </summary>
        SetArguments = 0x0100,

        /// <summary>
        ///     Gets or sets whether flow control will be allowed from an aspect by changing FlowBehavior.
        /// </summary>
        FlowControl = 0x0200,
        
        /// <summary>
        ///     Enables getting and setting the current return value from OnSuccess().
        ///     This does not affect methods where FlowControl.Return is respected.
        /// </summary>
        ReturnValue = 0x0400,

        /// <summary>
        ///     Gets or sets whether an aspect will be allowed to get and set the current yielded value in OnYield()
        ///     and OnResume() for iterators and async methods.
        /// </summary>
        YieldValue = 0x0800,

        /// <summary>
        ///     Gets or sets whether MethodInfo will be made available.
        /// </summary>
        Method = 0x1000,

        /// <summary>
        ///     Gets or sets whether the Tag property will be used to store user data between advice calls.
        /// </summary>
        Tag = 0x2000,

        /// <summary>
        ///     Enables all features.
        /// </summary>
        All = 0xFFFF,
    }
}
