namespace Rabotora.Core.Utility.Events;

public interface IValueEventArgs;

public interface IValueEventArgs<T> : IValueEventArgs where T : struct, IValueEventArgs<T>, allows ref struct;