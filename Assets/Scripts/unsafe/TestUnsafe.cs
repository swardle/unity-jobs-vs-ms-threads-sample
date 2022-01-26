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
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;



public class TestUnsafe : MonoBehaviour
{
    private NativeArray<float> arrayFloats;

    unsafe static float SumNativeArray(IntPtr p, int size, Allocator allocator
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        , AtomicSafetyHandle safety
#endif
        )
    {
        NativeArray<float> arrayFloats = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(p.ToPointer(), size, allocator);
        // don't need this in some builds https://forum.unity.com/threads/nativearrayunsafeutility-seems-to-not-work-in-builds.1153067/
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref arrayFloats, safety);
#endif
        float a = 0;
        for (int i = 0; i < arrayFloats.Length; i++)
        {
            a += arrayFloats[i];
        }
        return a;
    }

    unsafe static float SumNativeArray2(void* p, int size, Allocator allocator
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        , AtomicSafetyHandle safety
#endif
        )
    {
        NativeArray<float> arrayFloats = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(p, size, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref arrayFloats, safety);
#endif
        float a = 0;
        for (int i = 0; i < arrayFloats.Length; i++)
        {
            a += arrayFloats[i];
        }
        return a;
    }


    static float SumNativeArray3(NativeArrayUnsafeRef arrayFloat)
    {
        NativeArray<float> arrayFloats = NativeArrayExtensions.CreateNativeArray<float>(arrayFloat, Allocator.Invalid);
        float a = 0;
        for (int i = 0; i < arrayFloats.Length; i++)
        {
            a += arrayFloats[i];
        }
        return a;
    }


    void Update()
    {
        arrayFloats = new NativeArray<float>(10, Allocator.Persistent);

        for (int i = 0; i < 10; i++)
        {
            arrayFloats[i] = (float)i;
        }

        float acc = 0;

        SumNativeArray3(arrayFloats.GetNativeArrayUnsafeRef());

        unsafe
        {
            acc = SumNativeArray2(arrayFloats.GetUnsafePtr(), arrayFloats.Length, Allocator.Invalid
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , NativeArrayUnsafeUtility.GetAtomicSafetyHandle(arrayFloats)
#endif
                );
        }

        // Debug.Log(acc);
        arrayFloats.Dispose();
    }
}
