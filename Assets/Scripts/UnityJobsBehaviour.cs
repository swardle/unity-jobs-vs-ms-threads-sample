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
//    limitations under the License.using System;
using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Random = UnityEngine.Random;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;



public class UnityJobsBehaviour : MonoBehaviour
{
    public RawImage[] m_rawImages;
    public Toggle m_enableSystem;
    public Text m_timerText;
    private DropPool[] m_dropPools;
    private Texture2D[] m_textures;
    private int updateTick = 0;
    private Stopwatch m_timer = new Stopwatch();
    NativeArray<JobHandle>? jobHandles = null;
    TimeSpan updateTime = new TimeSpan(0);

    [BurstCompile]
    struct DropPool : IJob
    {
        public const int width = 512;
        public const int height = 512;
        public NativeArray<float> buffer1;
        public NativeArray<float> buffer2;
        public NativeArray<Color32> output;
        public NativeArray<int> frame;
        public NativeArray<Unity.Mathematics.Random> random;


        public void Clear()
        {
            random = new NativeArray<Unity.Mathematics.Random>(1, Allocator.Persistent);
            random[0] = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, 100000));

            buffer1 = new NativeArray<float>(width * height, Allocator.Persistent);
            buffer2 = new NativeArray<float>(width * height, Allocator.Persistent);
            output = new NativeArray<Color32>(width * height, Allocator.Persistent);
            frame = new NativeArray<int>(1, Allocator.Persistent);
            frame[0] = 0;

            Color32 gray = new Color32(0, 0, 0, 255);
            for (int y = 0; y < height; y++)
            {
                int yi = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = x + yi;
                    buffer1[i] = gray.r;
                    buffer2[i] = gray.r;
                    output[i] = gray;
                }
            }
        }

        void Rain()
        {
            var lrandom = random[0];
            int x = lrandom.NextInt(1, width - 1);
            int y = lrandom.NextInt(1, height - 1);
            random[0] = lrandom;
            NativeArray<float> current = buffer1;
            NativeArray<float> previous = buffer2;
            // swap buffers every frame
            if ((frame[0] & 1) == 1)
            {
                current = buffer2;
                previous = buffer1;
            }

            int yi = y * DropPool.width;
            int i = x + yi;
            previous[i] = 4096;
        }

        public void Execute()
        {
            fakeExecute();
        }

        // The code actually running on the job
        public void fakeExecute()
        {
            float dampening = 0.99f;
            Rain();
            NativeArray<float> current = buffer1;
            NativeArray<float> previous = buffer2;
            // swap buffers every frame
            if ((frame[0] & 1) == 1)
            {
                current = buffer2;
                previous = buffer1;
            }
            frame[0]++;
            float val;
            byte bval;

            for (int y = 1; y < height - 1; y++)
            {
                int yi = y * width;
                for (int x = 1; x < width - 1; x++)
                {
                    int i = x + yi;
                    val = dampening * (
                           (
                             previous[i - 1] +
                                          previous[i + 1] +
                                          previous[i - width] +
                                          previous[i + width] +

                                          previous[i - width - 1] +
                                          previous[i - width + 1] +
                                          previous[i + width - 1] +
                                          previous[i + width + 1]
                                        ) / 4 -
                                        current[i]);
                    bval = (byte)Mathf.Clamp(val, 0, 255);
                    current[i] = val;

                    output[i] = new Color32(bval, bval, bval, 255);
                }
            }
        }
    }

    public void Awake()
    {
        m_textures = new Texture2D[m_rawImages.Length];
        m_dropPools = new DropPool[m_rawImages.Length];
        for (int i = 0; i < m_rawImages.Length; i++)
        {
            ref DropPool dropPool = ref m_dropPools[i];
            dropPool.Clear();
            RawImage rawImage = m_rawImages[i];
            m_textures[i] = new Texture2D(DropPool.width, DropPool.height, TextureFormat.RGBA32, false);
            Texture2D texture = m_textures[i];

            texture.filterMode = FilterMode.Point;
            texture.SetPixelData(dropPool.output, 0, 0);
            texture.Apply();
            rawImage.texture = texture;
        }
    }
    public void OnDestroy()
    {
        if (jobHandles.HasValue)
        {
            JobHandle.CompleteAll(jobHandles.Value);
            jobHandles.Value.Dispose();
            jobHandles = null;
        }
        for (int i = 0; i < m_rawImages.Length; i++)
        {
            ref DropPool dropPool = ref m_dropPools[i];
            dropPool.buffer1.Dispose();
            dropPool.buffer2.Dispose();
            dropPool.output.Dispose();
            dropPool.frame.Dispose();
            dropPool.random.Dispose();
        }

    }

    public void Update()
    {
        m_timer.Restart();
        if (m_enableSystem.isOn)
        {
            for (int i = 0; i < m_rawImages.Length; i++)
            {
                RawImage rawImage = m_rawImages[i];
                Texture2D texture = m_textures[i];
                ref DropPool dropPool = ref m_dropPools[i];
                texture.SetPixelData(dropPool.output, 0, 0);
                texture.Apply();
                rawImage.texture = texture;
            }

            jobHandles = new NativeArray<JobHandle>(m_rawImages.Length, Allocator.Persistent);
            for (int i = 0; i < m_rawImages.Length; i++)
            {
                ref DropPool dropPool = ref m_dropPools[i];
                NativeArray<JobHandle> jobHandesRef = jobHandles.Value;
                jobHandesRef[i] = dropPool.Schedule();
            }
            JobHandle.ScheduleBatchedJobs();
        }
        m_timerText.text = $"Unity Job = {updateTime.TotalMilliseconds}";
    }

    public void LateUpdate()
    {
        if (jobHandles.HasValue)
        {
            JobHandle.CompleteAll(jobHandles.Value);
            jobHandles.Value.Dispose();
            jobHandles = null;
        }

        m_timer.Stop();
        updateTime = TimeSpan.FromTicks(m_timer.ElapsedTicks);
        updateTick++;
    }


    private Vector2 TryToClickPool(PointerEventData eventData, RawImage rawImage, int image)
    {
        Vector2 result = new Vector2(-1.0f, -1.0f);
        Vector2 clickPosition = eventData.position;
        RectTransform thisRect = rawImage.GetComponent<RectTransform>();

        bool inImage = RectTransformUtility.RectangleContainsScreenPoint(thisRect, clickPosition, null);
        if (inImage)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(thisRect, clickPosition, null, out result);
            result += thisRect.sizeDelta / 2;
            result /= thisRect.sizeDelta;
            result *= new Vector2(DropPool.width, DropPool.height);
            ref DropPool dropPool = ref m_dropPools[image];
            NativeArray<float> current = dropPool.buffer1;
            NativeArray<float> previous = dropPool.buffer2;
            // swap buffers every frame
            if ((dropPool.frame[0] & 1) == 1)
            {
                current = dropPool.buffer2;
                previous = dropPool.buffer1;
            }
            int x = (int)result.x;
            int y = (int)result.y;
            if (x == DropPool.width || x == 0 || y == 0 || y == DropPool.height)
            {
                return result;
            }
            int yi = y * DropPool.width;
            int i = x + yi;
            previous[i] = 4096;

            return result;
        }
        return result;
    }

    public void MyOnClick(BaseEventData e)
    {
        PointerEventData eventData = (PointerEventData)e;
        for (int i = 0; i < m_rawImages.Length; i++)
        {
            RawImage rawImage = m_rawImages[i];
            TryToClickPool(eventData, rawImage, i);
        }
    }
}
