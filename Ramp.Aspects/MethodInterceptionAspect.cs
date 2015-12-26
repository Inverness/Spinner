﻿using System;

namespace Ramp.Aspects
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
    public class MethodInterceptionAspect : Aspect, IMethodInterceptionAspect
    {
        public virtual void OnInvoke(MethodInterceptionArgs args)
        {

        }
    }
}
