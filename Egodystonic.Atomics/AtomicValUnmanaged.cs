﻿// (c) Egodystonic Studios 2018
// Author: Ben Bowen
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Egodystonic.Atomics {
	/// <summary>
	/// TODO document the max struct size and also the fact that IEquatable overrides are not used here (if they are required, AtomicVal should be used)
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public sealed unsafe class AtomicValUnmanaged<T> : IAtomic<T> where T : unmanaged {
		long _valueAsLong;

		public T Value {
			[MethodImpl(MethodImplOptions.AggressiveInlining)] get => Get();
			[MethodImpl(MethodImplOptions.AggressiveInlining)] set => Set(value);
		}

		public AtomicValUnmanaged() : this(default) { }
		public AtomicValUnmanaged(T initialValue) {
			if (sizeof(T) > sizeof(long)) {
				throw new ArgumentException($"Generic type parameter in {typeof(AtomicValUnmanaged<>).Name} must not exceed {sizeof(long)} bytes. " +
											$"Given type '{typeof(T)}' has a size of {sizeof(T)} bytes. " +
											$"Use {typeof(AtomicVal<>).Name} instead for large unmanaged types.");
			}
			Set(initialValue);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T Get() {
			var valueCopy = GetLong();
			return ReadFromLong(&valueCopy);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		long GetLong() {
			if (IntPtr.Size == sizeof(long)) return Volatile.Read(ref _valueAsLong);
			else return Interlocked.Read(ref _valueAsLong);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T GetUnsafe() {
			var valueCopy = _valueAsLong;
			return ReadFromLong(&valueCopy);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Set(T newValue) {
			long newValueAsLong;
			WriteToLong(&newValueAsLong, newValue);
			SetLong(newValueAsLong);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void SetLong(long newValueAsLong) {
			if (IntPtr.Size == sizeof(long)) Volatile.Write(ref _valueAsLong, newValueAsLong);
			else Interlocked.Exchange(ref _valueAsLong, newValueAsLong);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetUnsafe(T newValue) {
			long newValueAsLong;
			WriteToLong(&newValueAsLong, newValue);
			_valueAsLong = newValueAsLong;
		}

		public T SpinWaitForValue(T targetValue) {
			var spinner = new SpinWait();
			long targetValueAsLong;
			WriteToLong(&targetValueAsLong, targetValue);

			while (true) {
				var curValueAsLong = GetLong();
				if (curValueAsLong == targetValueAsLong) return ReadFromLong(&curValueAsLong);
				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T FastExchange(T newValue) {
			long newValueAsLong;
			WriteToLong(&newValueAsLong, newValue);
			var previousValueAsLong = Interlocked.Exchange(ref _valueAsLong, newValueAsLong);
			return ReadFromLong(&previousValueAsLong);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public (T PreviousValue, T CurrentValue) Exchange(T newValue) {
			long newValueAsLong;
			WriteToLong(&newValueAsLong, newValue);
			var previousValueAsLong = Interlocked.Exchange(ref _valueAsLong, newValueAsLong);
			return (ReadFromLong(&previousValueAsLong), newValue);
		}

		public (T PreviousValue, T CurrentValue) Exchange<TContext>(Func<T, TContext, T> mapFunc, TContext context) {
			var spinner = new SpinWait();

			while (true) {
				var curValueAsLong = GetLong();
				var newValue = mapFunc(ReadFromLong(&curValueAsLong), context);
				long newValueAsLong;
				WriteToLong(&newValueAsLong, newValue);

				if (Interlocked.CompareExchange(ref _valueAsLong, newValueAsLong, curValueAsLong) == curValueAsLong) return (ReadFromLong(&curValueAsLong), newValue);
				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T FastTryExchange(T newValue, T comparand) {
			long newValueAsLong, comparandAsLong;
			WriteToLong(&newValueAsLong, newValue);
			WriteToLong(&comparandAsLong, comparand);
			var previousValueAsLong = Interlocked.CompareExchange(ref _valueAsLong, newValueAsLong, comparandAsLong);
			return ReadFromLong(&previousValueAsLong);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public (bool ValueWasSet, T PreviousValue, T CurrentValue) TryExchange(T newValue, T comparand) {
			long newValueAsLong, comparandAsLong;
			WriteToLong(&newValueAsLong, newValue);
			WriteToLong(&comparandAsLong, comparand);
			var previousValueAsLong = Interlocked.CompareExchange(ref _valueAsLong, newValueAsLong, comparandAsLong);
			var previousValue = ReadFromLong(&previousValueAsLong);

			var wasSet = previousValueAsLong == comparandAsLong;
			return (wasSet, previousValue, wasSet ? newValue : previousValue);
		}

		public (T PreviousValue, T CurrentValue) SpinWaitForExchange(T newValue, T comparand) {
			var spinner = new SpinWait();
			long newValueAsLong, comparandAsLong;
			WriteToLong(&newValueAsLong, newValue);
			WriteToLong(&comparandAsLong, comparand);

			while (true) {
				if (Interlocked.CompareExchange(ref _valueAsLong, newValueAsLong, comparandAsLong) == comparandAsLong) return (comparand, newValue);
				spinner.SpinOnce();
			}
		}
		public (T PreviousValue, T CurrentValue) SpinWaitForExchange<TContext>(Func<T, TContext, T> mapFunc, TContext context, T comparand) {
			var spinner = new SpinWait();
			var newValue = mapFunc(comparand, context); // curValue will always be comparand when this method returns
			long newValueAsLong, comparandAsLong;
			WriteToLong(&newValueAsLong, newValue);
			WriteToLong(&comparandAsLong, comparand);

			while (true) {
				if (Interlocked.CompareExchange(ref _valueAsLong, newValueAsLong, comparandAsLong) == comparandAsLong) return (comparand, newValue);
				spinner.SpinOnce();
			}
		}
		public (T PreviousValue, T CurrentValue) SpinWaitForExchange<TMapContext, TPredicateContext>(Func<T, TMapContext, T> mapFunc, TMapContext mapContext, Func<T, T, TPredicateContext, bool> predicate, TPredicateContext predicateContext) {
			var spinner = new SpinWait();
			
			while (true) {
				var curValue = Get();
				var newValue = mapFunc(curValue, mapContext);
				if (!predicate(curValue, newValue, predicateContext)) {
					spinner.SpinOnce();
					continue;
				}

				long curValueAsLong, newValueAsLong;
				WriteToLong(&curValueAsLong, curValue);
				WriteToLong(&newValueAsLong, newValue);

				if (Interlocked.CompareExchange(ref _valueAsLong, newValueAsLong, curValueAsLong) == curValueAsLong) return (curValue, newValue);
				spinner.SpinOnce();
			}
		}

		public (bool ValueWasSet, T PreviousValue, T CurrentValue) TryExchange<TContext>(Func<T, TContext, T> mapFunc, TContext context, T comparand) {
			long comparandAsLong, newValueAsLong;
			WriteToLong(&comparandAsLong, comparand);
			var newValue = mapFunc(comparand, context); // Comparand will always be curValue if the interlocked call passes
			WriteToLong(&newValueAsLong, newValue);

			var prevValueAsLong = Interlocked.CompareExchange(ref _valueAsLong, newValueAsLong, comparandAsLong);
			var prevValue = ReadFromLong(&prevValueAsLong);
			if (prevValueAsLong == comparandAsLong) return (true, prevValue, newValue);
			else return (false, prevValue, prevValue);
		}

		public (bool ValueWasSet, T PreviousValue, T CurrentValue) TryExchange<TMapContext, TPredicateContext>(Func<T, TMapContext, T> mapFunc, TMapContext mapContext, Func<T, T, TPredicateContext, bool> predicate, TPredicateContext predicateContext) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				long curValueAsLong;
				WriteToLong(&curValueAsLong, curValue);
				var newValue = mapFunc(curValue, mapContext);
				if (!predicate(curValue, newValue, predicateContext)) return (false, curValue, curValue);

				long newValueAsLong;
				WriteToLong(&newValueAsLong, newValue);

				if (Interlocked.CompareExchange(ref _valueAsLong, newValueAsLong, curValueAsLong) == curValueAsLong) return (true, curValue, newValue);
				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void WriteToLong(long* target, T val) {
			*((T*) target) = val;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static T ReadFromLong(long* src) {
			return *((T*) src);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static implicit operator T(AtomicValUnmanaged<T> operand) => operand.Get();

		public override string ToString() => Get().ToString();
	}
}
