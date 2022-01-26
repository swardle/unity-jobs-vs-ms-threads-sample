//    Copyright 2022 Google LLC
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        https://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;
using System.Runtime.InteropServices;


public class MyVoidPointer<T> where T : struct
{
    GCHandle m_handle;

    public MyVoidPointer(ref T t) { m_handle = GCHandle.Alloc(t, GCHandleType.Pinned); }

    public IntPtr m_ptr { get { return m_handle.AddrOfPinnedObject(); } }

    public void Dispose() { m_handle.Free(); }

    public static unsafe T GetObject(IntPtr ptr)
    {
        T ret;
        UnsafeUtility.CopyPtrToStructure(ptr.ToPointer(), out ret);
        return ret;
    }


    public static unsafe void SetObject(ref T t, IntPtr ptr)
    {
        UnsafeUtility.CopyStructureToPtr(ref t, ptr.ToPointer());
    }

}

public struct NativeArrayUnsafeRef
{
    public IntPtr p;
    public int size;
    // don't need this in some builds https://forum.unity.com/threads/nativearrayunsafeutility-seems-to-not-work-in-builds.1153067/
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    public AtomicSafetyHandle safety;
#endif
}

unsafe public struct BlitableRef<T> : IDisposable where T : struct
{
    private void* m_Buffer;
    private Allocator m_AllocatorLabel;

    unsafe public BlitableRef(Allocator allocator)
    {
        m_AllocatorLabel = allocator;
        var elementSize = UnsafeUtility.SizeOf<T>();
        m_Buffer = UnsafeUtility.Malloc(elementSize, UnsafeUtility.AlignOf<T>(), allocator);
    }

    unsafe public BlitableRef(void* dataPointer, Allocator allocator)
    {
        m_Buffer = dataPointer;
        m_AllocatorLabel = allocator;
    }

    unsafe public void Dispose()
    {
        UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
    }

    public ref T GetRef()
    {
        return ref UnsafeUtility.ArrayElementAsRef<T>(m_Buffer, 0);
    }

    unsafe public T this[int index]
    {
        get
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_AllocatorLabel == Allocator.Invalid)
                throw new ArgumentException("AutoGrowArray was not initialized.");
#endif
            return UnsafeUtility.ReadArrayElement<T>(m_Buffer, index);
        }

        set
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_AllocatorLabel == Allocator.Invalid)
                throw new ArgumentException("AutoGrowArray was not initialized.");
#endif
            UnsafeUtility.WriteArrayElement(m_Buffer, index, value);
        }
    }

    public unsafe void* GetUnsafePtr()
    {
        return m_Buffer;
    }
}

public static class BlitableArrayExtensions
{
    public static unsafe BlitableRef<T> ConvertExistingDataToBlitableRef<T>(void* dataPointer, Allocator allocator) where T : struct
    {
        var newArray = new BlitableRef<T>(dataPointer, allocator);
        return newArray;
    }
}

public static class NativeArrayExtensions
{

    public static ref T GetRef<T>(this NativeArray<T> array, int index) where T : struct
    {
        // You might want to validate the index first, as the unsafe method won't do that.
        if (index < 0 || index >= array.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        unsafe
        {
            return ref UnsafeUtility.ArrayElementAsRef<T>(array.GetUnsafePtr(), index);
        }
    }

    public static unsafe NativeArrayUnsafeRef GetNativeArrayUnsafeRef<T>(this NativeArray<T> array) where T : struct
    {
        return new NativeArrayUnsafeRef()
        {
            p = new IntPtr(array.GetUnsafePtr()),
            size = array.Length,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            safety = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(array)
#endif
        };
    }

    public static unsafe NativeArray<T> CreateNativeArray<T>(NativeArrayUnsafeRef parm, Allocator allocator) where T : struct
    {
        NativeArray<T> array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(parm.p.ToPointer(), parm.size, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, parm.safety);
#endif
        return array;

    }

    public static unsafe IntPtr GetIntPtr<T>(this NativeArray<T> array) where T : struct
    {
        return new IntPtr(array.GetUnsafePtr());
    }
}

// https://forum.unity.com/threads/can-i-use-nativearray-in-burst-compiled-method.1142542/
// https://forum.unity.com/threads/how-to-use-nativearrayunsafeutility-convertexistingdatatonativearray-tvalue.1049015/
// https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/NativeArray/NativeArray.cs
// https://forum.unity.com/threads/trying-to-create-readonly-array-with-atomicsafetyhandle.800976/

