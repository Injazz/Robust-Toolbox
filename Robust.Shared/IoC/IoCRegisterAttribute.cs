using System;
using Robust.Shared.Interfaces.GameObjects;

namespace Robust.Shared.IoC
{

    /// <summary>
    ///     Marks a component as being automatically registered by <see cref="IoCManager.RegisterAssembly" />
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Module, AllowMultiple = true)]
    public sealed class IoCRegisterAttribute : Attribute
    {

        private readonly string _contractType;

        private readonly string _concreteTypeStr;

        public Type ContractType => Type.GetType(_contractType);
        public Type ConcreteType => Type.GetType(_concreteTypeStr);

        public IoCRegisterAttribute(Type contractType, Type concreteType)
        {
            _contractType = contractType.AssemblyQualifiedName;
            _concreteTypeStr = concreteType.AssemblyQualifiedName;
        }

    }

}
