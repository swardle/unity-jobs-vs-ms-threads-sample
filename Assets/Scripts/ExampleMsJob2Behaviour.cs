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
//    limitations under the License.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

public class ExampleMsJob2Behaviour : MonoBehaviour
{
    private DropPoolManaged m_dropPoolManaged;

    // Common interface for executing something
    interface MyTask
    {
        void Execute();
    }

    private struct Job : IJob
    {
        public GCHandle m_handle;

        public void Execute()
        {
            MyTask task = (MyTask)m_handle.Target;
            task.Execute();
        }
    }

    class DropPoolManaged : MyTask
    {
        // DropPoolManaged Job data
        public GCHandle m_gcHandle; // a handle to track DropPoolManaged's data
        public JobHandle m_jobHandle; // DropPoolManaged job handle
        public Stopwatch m_timer = new Stopwatch(); // burst does not have timer

        private delegate void ExecuteStaticDelegate(IntPtr param); // Delegate type

        public GCHandle m_dropPoolBurstCallHandle; // Handle to pin DropPool for burst
        public DropPoolBurstCall m_dropPoolBurstCall; // A struct with a lot of NativeArrays
        private ExecuteStaticDelegate m_FunctionPtr; // Delegate AKA function pointer

        public DropPoolManaged()
        {
            m_dropPoolBurstCall.Init();
            m_dropPoolBurstCallHandle = GCHandle.Alloc(m_dropPoolBurstCall, GCHandleType.Pinned);
            // I was thinking maybe I should do this but meh I get a warning around a memory leak
            //unsafe
            //{
            //    ref DropPoolBurstCall dropPool = ref UnsafeUtility.ArrayElementAsRef<DropPoolBurstCall>(m_dropPoolBurstCallHandle.AddrOfPinnedObject().ToPointer(), 0);
            //    dropPool.Init();
            //}
            
            // Create a Invoke able function pointer
            m_FunctionPtr = BurstCompiler.CompileFunctionPointer<ExecuteStaticDelegate>(DropPoolBurstCall.ExecuteStatic).Invoke;
            // Add pools to the GC as they will be used in a job.
            m_gcHandle = GCHandle.Alloc(this);
        }
        public void Dispose()
        {
            // oddly this does not work
            // ObjectDisposedException: The UNKNOWN_OBJECT_TYPE has been deallocated, it is not allowed to access it
            // DropPoolBurstCall dropPool;
            // IntPtr ptr = m_dropPoolBurstCallHandle.AddrOfPinnedObject();
            // unsafe { UnsafeUtility.CopyPtrToStructure(ptr.ToPointer(), out dropPool); }
            // dropPool.Dispose();
            m_dropPoolBurstCall.Dispose();

            // oddly this does not work
            // ObjectDisposedException: The UNKNOWN_OBJECT_TYPE has been deallocated, it is not allowed to access it
            //unsafe
            //{
            // ref DropPoolBurstCall dropPool = ref UnsafeUtility.ArrayElementAsRef<DropPoolBurstCall>(m_dropPoolBurstCallHandle.AddrOfPinnedObject().ToPointer(), 0);
            //    dropPool.Dispose();
            //}


            m_dropPoolBurstCallHandle.Free();
            m_gcHandle.Free();
        }

        public void Execute()
        {
            m_timer.Restart();
            m_FunctionPtr(m_dropPoolBurstCallHandle.AddrOfPinnedObject());
            m_timer.Stop();
            unsafe
            {
                ref DropPoolBurstCall dropPool = ref UnsafeUtility.ArrayElementAsRef<DropPoolBurstCall>(m_dropPoolBurstCallHandle.AddrOfPinnedObject().ToPointer(), 0);
                if (dropPool.m_output.Length == 3)
                {
                    UnityEngine.Debug.LogError("Should be 4");
                }
            }
        }
    }

    [BurstCompile]
    struct DropPoolBurstCall
    {
        public NativeArray<Unity.Mathematics.Random> m_random;
        public NativeArray<float> m_output;

        public void Init()
        {
            m_random = new NativeArray<Unity.Mathematics.Random>(1, Allocator.Persistent);
            m_random[0] = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, 100000));
            float[] initData = { 1.0f, 2.0f, 3.0f };
            m_output = new NativeArray<float>(initData, Allocator.Persistent);
        }

        public void Dispose()
        {
            m_random.Dispose();
            m_output.Dispose();
        }

        public void ExecuteBurstCall()
        {
            var lrandom = m_random[0];
            lrandom.NextInt(1, 1000);
            m_output = new NativeArray<float>(4, Allocator.Temp);
            m_output[0] = 1.0f;
            m_output[1] = 2.0f;
            m_output[2] = 3.0f;
            m_output[3] = 4.0f;
        }

        unsafe struct NonBlittableStruct
        {
            public string stringValue;

            public float* testPtr;

            public int testInt;
        }

        [BurstCompile]
        unsafe public static void ExecuteStatic(IntPtr ptr)
        {
            ref DropPoolBurstCall dropPool = ref UnsafeUtility.ArrayElementAsRef<DropPoolBurstCall>(ptr.ToPointer(), 0);
            dropPool.ExecuteBurstCall();
        }
    }

    public void Awake()
    {
        NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        m_dropPoolManaged = new DropPoolManaged();
    }

    public void OnDestroy()
    {
        m_dropPoolManaged.m_jobHandle.Complete();
        m_dropPoolManaged.Dispose();
    }

    public void Update()
    {
        Job managedJob = new Job()
        {
            m_handle = m_dropPoolManaged.m_gcHandle
        };

        // We remember the JobHandle so we can complete it later
        m_dropPoolManaged.m_jobHandle = managedJob.Schedule();
        JobHandle.ScheduleBatchedJobs();
    }

    public void LateUpdate()
    {
        m_dropPoolManaged.m_jobHandle.Complete();
    }

}
