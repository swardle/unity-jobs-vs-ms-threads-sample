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
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Jobs;


public class ExampleMsJobsBehaviour : MonoBehaviour
{
    private DropPool m_dropPool;
    private GCHandle m_gcHandle;
    private JobHandle m_jobHandle;

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

    class DropPool : MyTask
    {
        public Stopwatch m_timer = new Stopwatch();

        public void Execute()
        {
            m_timer.Restart();
            // Real code goes here
            m_timer.Stop();
        }
    }

    public void Awake()
    {
        m_dropPool = new DropPool();
        // Add pools to the GC as they will be used in a job.
        m_gcHandle = GCHandle.Alloc(m_dropPool);
    }

    public void OnDestroy()
    {
        m_jobHandle.Complete();
        m_gcHandle.Free();
    }

    public void Update()
    {
        Job job = new Job()
        {
            m_handle = m_gcHandle
        };
        m_jobHandle = job.Schedule();
        JobHandle.ScheduleBatchedJobs();
    }

    public void LateUpdate()
    {
        m_jobHandle.Complete();
    }

}
