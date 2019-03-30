using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using TreeManager;
using Pvr_UnitySDKAPI;

public class TreeController : MonoBehaviour
{
    //**********************场景相关的properties************************************/
    // Pico设备
    public GameObject m_picoSystem;  // Pico System
    public GameObject m_headCamera;  // 头盔 HeadCamera
    public GameObject m_controller0; // 手柄 Controller
    private Ray m_ctrlRay;           // 手柄的方向

    // UI控件
    public GameObject m_canvas;      // canvas: UI控件的幕布
    public Button m_btnIterateOnce;  // 按钮: Iterate for Once 迭代一次按钮
    public Button m_btnClearAll;     // 按钮: Clear all data 清除按钮
    public Toggle m_toggleLasso;     // 开关按钮: 是否绘制Lasso的check box
    public Toggle m_toggleFreeSketch;// 开关按钮：是否进行FreeSketck的check box

    // 场景中的物体们
    public ParticleSystem m_particleSystem;  // 粒子系统（生成植物的绿光效果）

    public GameObject m_laserRender;      // 手柄发出的射线Laser
    public GameObject m_laserBall;        // 手柄射线末端的小红球（后期会删掉）
    public GameObject m_lassoRender;      // 套索Lasso的GameObject
    private List<Vector3> m_lassoPoints;  // 套索Lasso上的所有的点
    public GameObject m_SketchRender;     // 手绘枝干（FreeSketch）的GameObject
    private List<Vector3> m_sketchPoints;  // Free Sketch绘制的坐标点

    public Material m_leafMtl;       // 材质：树叶的材质
    public Material m_barkMtl;       // 材质：树干的材质

    //**********************Tree Modeling相关的properties************************************/

    // tree & algorithm
    private Tree3D m_tree;          // 【重中之重】我们的tree modeling算法都在这里
    private List<GameObject> m_treePart;        // 表示树干部分的GameObject
    private List<GameObject> m_leafPart;        // 表示树叶部分的GameObject
    private List<GameObject> m_selectedPart;    // 表示被“选中”部分的树干的GameObject;

    // 定义了手柄绘制时的目标"平面"信息（n为平面法线，a0为平面上一点）
    private Vector3 m_a0;
    private Vector3 m_n;

    // 【开关】
    private bool m_isLassoMouseDone = false; // 手柄是否在绘制套索Lasso
    private bool m_isFreeSketched = false;   // 手柄是否在进行FreeSketch

    private Thread m_thread;   // 【线程】，因为生成算法耗时，因此生成过程放在线程中完成

    // 无需关注的属性
    public Button m_btbTest;       // 【UI测试】（备注：目前UI点击事件在Pico中无效）
    public Text m_Text;
    public Transform m_ctrldirection;

    /// <summary>
    /// Start()函数是在程序开始是执行的初始化函数
    /// </summary>
    void Start()
    {
        m_tree = new Tree3D();
        m_tree.InitTree();    // 创建最初的芽

        m_thread = null;
        m_treePart = new List<GameObject>();
        m_leafPart = new List<GameObject>();
        m_selectedPart = new List<GameObject>();
        m_lassoPoints = new List<Vector3>();
        m_sketchPoints = new List<Vector3>();

        m_ctrlRay = new Ray();

        this.GetComponent<MeshFilter>().mesh.Clear();

        m_btbTest.onClick.AddListener(OnClick); 
    }

    /// <summary>
    /// Update()函数每一帧的更新函数（以很高的频率循环被调用）
    /// </summary>
    void Update()
    {
        RayCasting_Controller();   // 【重要】处理pico手柄的事件

        if(IsThreadFinished())    // 如果更新植物数据的线程结束了，则创建新的模型
        {
            UpdateTreeObjects();  // 根据当前的m_tree生成新的Mesh模型

            m_thread = null;      // 结束线程

            // 清除粒子系统（绿色的光）
            m_particleSystem.Clear();     
            var em = m_particleSystem.emission;
            em.enabled = false;

            // 解出事件锁
            m_tree.m_isUpdateMesh = false;    
            m_tree.m_isUpdateMeshDone = true;
        }
    }

    /// <summary>
    /// 清除所有的数据
    /// </summary>
    public void ClearAllData()
    {
        foreach (GameObject m in m_treePart)   // 我也不知道C#会不会自动释放，但是保险起见还是手动release掉了
            Destroy(m);
        foreach (GameObject m in m_leafPart)
            Destroy(m);
        foreach (GameObject m in m_selectedPart)
            Destroy(m);

        m_treePart.Clear();
        m_leafPart.Clear();
        m_selectedPart.Clear();  
        
        m_tree.ClearAllData();
    }

    /// <summary>
    /// 点击“Iterate for Once”按钮的事件
    /// </summary>
    public void OnClick_IterateOnce()
    {
        if (m_thread != null && m_thread.ThreadState == ThreadState.Running)
            return;
        if (m_tree.m_isUpdateMesh)
            return;
        if (!m_tree.m_isUpdateMeshDone)
            return;
        if (m_tree.m_growthMode != Tree3D.GrowthMode._Free)
            return;

        // Create MarkerPoints
        m_tree.RecreateMarkerPoints();

        // 开启事件锁
        m_tree.m_isUpdateMesh = false;
        m_tree.m_isUpdateMeshDone = false;

        // 开启粒子系统
        var em = m_particleSystem.emission;
        em.enabled = true;

        List<Vector3> location = m_tree.GetBudLocationList();

        // 更新粒子系统的数据
        ParticleSystem.Particle[] particles_arr = new ParticleSystem.Particle[location.Count];
        m_particleSystem.Emit(location.Count);
        m_particleSystem.GetParticles(particles_arr);

        for (int i = 0; i < location.Count; i++)
        {
            particles_arr[i].position = location[i];
        }
        // build the particle system
        m_particleSystem.SetParticles(particles_arr, particles_arr.Length);
        em.enabled = false;

        // 开启线程，进行植物的生成
        m_thread = new Thread(new ThreadStart(SubThreadIterOnce));
        m_thread.Start();
    }

    /// <summary>
    /// 在Lasso模式下，释放鼠标会根据当前Lasso生成新的植物
    /// </summary>
    public void OnMouseRelease_LassoMode()
    {
        if (m_thread != null && m_thread.ThreadState == ThreadState.Running)
            return;
        if (m_tree.m_isUpdateMesh)
            return;
        if (!m_tree.m_isUpdateMeshDone)
            return;
        if (m_tree.m_growthMode != Tree3D.GrowthMode._Lasso)
            return;

        // Create MarkerPoints
        Vector3 dir = new Vector3(m_controller0.transform.position.x, 0.0f, m_controller0.transform.position.z);
        m_tree.RecreateMarkerPointByLasso(m_lassoPoints,
            Vector3.Cross(dir, new Vector3(0.0f, 1.0f, 0.0f)).normalized,
            dir.normalized);

        // update particle system
        m_tree.m_isUpdateMesh = false;
        m_tree.m_isUpdateMeshDone = false;

        var em = m_particleSystem.emission;
        em.enabled = true;

        List<float[]> location = m_tree.m_KDmarkerPoints;

        ParticleSystem.Particle[] particles_arr = new ParticleSystem.Particle[100];
        m_particleSystem.Emit(particles_arr.Length);
        m_particleSystem.GetParticles(particles_arr);

        for (int i = 0; i < 100; i++)
            particles_arr[i].position = new Vector3(location[i][0], location[i][1], location[i][2]);

        // build the particle system
        m_particleSystem.SetParticles(particles_arr, particles_arr.Length);
        em.enabled = false;

        // 开启生成植物的线程
        m_thread = new Thread(new ThreadStart(SubThreadLasso));
        m_thread.Start();
    }

    /*******子线程：点击按钮“Iterate for once”后触发***/
    private void SubThreadIterOnce()
    {
        m_tree.btn_iterateOnce();  // 生成植物的一轮迭代
    }

    /*******子线程：Lasso模式下，释放鼠标后触发*************************/
    private void SubThreadLasso()
    {
        while(true)  //不断地进行迭代，直到植物不再生长为止
        {
            m_tree.m_isModified = false; 
            m_tree.btn_iterateOnce();

            if (!m_tree.m_isModified)  // 如果本轮迭代tree的数据没有得到更新，结束循环
                break;
        }
    }


    /********返回当前线程是否结束**************/
    private bool IsThreadFinished()
    {
        return m_thread!=null && this.m_tree.m_isUpdateMesh && m_thread.ThreadState == ThreadState.Stopped;
    }

    /// <summary>
    /// 根据当前的m_tree信息更新树木的GameObject
    /// </summary>
    private void UpdateTreeObjects()
    {
        // 清除原有的tree parts
        foreach (GameObject m in m_treePart)
            Destroy(m);
        m_treePart.Clear();

        // 设置新的tree parts
        List<Mesh> meshes = m_tree.GetMesh(); // Get meshes from 'Tree'
        for (int i = 0; i < meshes.Count; i++)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.transform.position = new Vector3(0.0f, 0.0f, 0.0f);
            //obj.name = "Mesh_" + i.ToString();
            obj.GetComponent<MeshFilter>().mesh = meshes[i];
            obj.GetComponent<MeshRenderer>().material = new Material(GetComponent<MeshRenderer>().material);
            obj.tag = "Mesh";
            MeshCollider collider = obj.AddComponent(typeof(MeshCollider)) as MeshCollider;

            m_treePart.Add(obj);
        }

        // 清除原有的leaf parts;
        foreach (GameObject m in m_leafPart)
            Destroy(m);
        m_leafPart.Clear();

        // 设置新的tree parts
        List<Mesh> leafMeshes = m_tree.GetLeafMesh(); // Get meshes from 'Leaf'

        for (int i = 0; i < leafMeshes.Count; i++)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            //obj.name = "Mesh_" + i.ToString();
            obj.GetComponent<MeshFilter>().mesh = leafMeshes[i];
            obj.GetComponent<MeshRenderer>().material = m_leafMtl;

            m_leafPart.Add(obj);
        }
    }

    /// <summary>
    /// 处理Pico手柄的事件：移动，点击等
    /// </summary>
    private void RayCasting_Controller()
    {
        // 更新controller的方向信息m_ctrlRay
        m_ctrlRay.origin = m_controller0.transform.position;
        m_ctrlRay.direction = m_ctrldirection.position - m_controller0.transform.position;

        // 处理一些手柄的交互式操作
        DrawingLasso();
        DrawingFreeSketch();

        // 进行raycast
        RaycastHit hitInfo;
        if (Physics.Raycast(m_ctrlRay, out hitInfo))  // 首先保证线程thread停止，如果指向某个collider
        {
            GameObject clickObj = hitInfo.collider.gameObject;

            // 更新手柄射线Laser信息
            m_laserRender.GetComponent<LineRenderer>().SetPosition(0, m_controller0.transform.position);
            m_laserRender.GetComponent<LineRenderer>().SetPosition(1, hitInfo.point);

            // 更新手柄射线末端的红球球
            m_laserBall.transform.position = hitInfo.point;

            if (this.m_thread == null)  // 如果当前没有正在处理的线程thread，就处理TouchPad的点集事件
            {
                // 如果发生了TouchPad的点击事件
                if (Controller.UPvr_GetKeyDown(0, Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonDown(0))
                {
                    // 【1】如果是选择了FreeSketch模式，选择一段枝干，即可开始绘制
                    if(m_toggleFreeSketch.isOn && clickObj.CompareTag("Mesh"))
                    {
                        m_n = (m_picoSystem.transform.position - hitInfo.point).normalized;
                        m_a0 = hitInfo.point;

                        m_isFreeSketched = true;
                    }

                    // 【2】如果点击了IterateOnce按钮
                    if (m_btnIterateOnce.name == clickObj.name)      
                        OnClick_IterateOnce();

                    // 【3】如果点击了CheckBox Lasso，开启Lasso绘制模式
                    if (m_toggleLasso.name == clickObj.name)   
                    {
                        m_toggleLasso.isOn = !m_toggleLasso.isOn;

                        // 删除套索Lasso的点
                        m_lassoPoints.Clear();
                        m_lassoRender.GetComponent<LineRenderer>().positionCount = 0;

                        if (m_toggleLasso.isOn)   // 修改植物生长的参数
                            m_tree.SetupParameters(Tree3D.GrowthMode._Lasso);
                        else
                            m_tree.SetupParameters(Tree3D.GrowthMode._Free);
                         
                        if (m_toggleLasso.isOn)  // （暂时不允许两个Toggle同时被勾选）
                            m_toggleFreeSketch.isOn = false;

                    }
                    // 【4】如果点击了CheckBox FreeSketch，开启FreeSketch绘制模式
                    if (m_toggleFreeSketch.name == clickObj.name)
                        m_toggleFreeSketch.isOn = !m_toggleFreeSketch.isOn;

                    // 【5】如果点击了ClearAll按钮，清除所有数据
                    if (m_btnClearAll.name == clickObj.name) 
                        ClearAllData();
                }
            }
        }
        else // 如果没有指向某个collider
        {
            // 更新手柄球球和laser
            m_laserBall.transform.position = m_ctrlRay.origin + m_ctrlRay.direction.normalized * 100;

            m_laserRender.GetComponent<LineRenderer>().SetPosition(0, m_controller0.transform.position);
            m_laserRender.GetComponent<LineRenderer>().SetPosition(1, m_ctrlRay.origin+m_ctrlRay.direction.normalized * 100);
        }

        // 处理Touching Pad的Moving Event
        //if((Controller.UPvr_GetKey(0,Pvr_KeyCode.TRIGGER) && Controller.UPvr_IsTouching(0)))
        {
            if(Controller.UPvr_GetSwipeDirection(0) == SwipeDirection.SwipeLeft  || Input.GetKey(KeyCode.A))
            {
                m_picoSystem.transform.RotateAround(new Vector3(0.0f,0.0f,0.0f),new Vector3(0.0f, 1.0f, 0.0f), 0.5f);
                m_canvas.transform.RotateAround(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), 0.5f);
            }
            if (Controller.UPvr_GetSwipeDirection(0) == SwipeDirection.SwipeRight || Input.GetKey(KeyCode.D))
            {
                m_picoSystem.transform.RotateAround(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), -0.5f);
                m_canvas.transform.RotateAround(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), -0.5f);
            }
            if (Controller.UPvr_GetSwipeDirection(0) == SwipeDirection.SwipeUp || Input.GetKey(KeyCode.W))
            {
                Vector3 t = m_headCamera.GetComponent<Camera>().transform.forward.normalized;
                Vector3 dir = new Vector3(t.x,0.0f,t.z);
                m_picoSystem.transform.Translate(0.5f*dir,Space.World);
                m_canvas.transform.Translate(0.5f * dir, Space.World);
            }
            if (Controller.UPvr_GetSwipeDirection(0) == SwipeDirection.SwipeDown || Input.GetKey(KeyCode.S))
            {
                Vector3 t = m_headCamera.GetComponent<Camera>().transform.forward.normalized;
                Vector3 dir = -new Vector3(t.x, 0.0f, t.z);
                m_picoSystem.transform.Translate(0.5f * dir, Space.World);
                m_canvas.transform.Translate(0.5f * dir, Space.World);
            }
        }
    }

    /***************一些手柄的交互式操作*****************/
    private void DrawingLasso()
    {
        // 处理鼠标点击事件（开始绘制Lasso)
        if (m_toggleLasso.isOn &&
            (Controller.UPvr_GetKeyDown(0, Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonDown(0)))
        {
            m_lassoPoints.Clear();
            m_lassoRender.GetComponent<LineRenderer>().positionCount = 0;

            m_isLassoMouseDone = true;

            m_n = (m_picoSystem.transform.position - new Vector3(0, 0, 0)).normalized;
            m_n.y = 0;
            m_a0 = new Vector3(0, 0, 0);
        }
        // 处理鼠标释放事件（Lasso绘制结束）
        if (m_isLassoMouseDone &&
            (Controller.UPvr_GetKeyUp(0, Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonUp(0)))
        {
            m_isLassoMouseDone = false;

            ClearAllData();

            if (m_lassoPoints.Count > 5)  // Lasso结点足够多，则根据Lasso创建植物
                OnMouseRelease_LassoMode();
        }
        // 处理鼠标滑动事件2（绘制Lasso中）
        if (m_isLassoMouseDone)
        {
            Vector3 p0 = m_ctrlRay.origin;
            Vector3 u = m_ctrlRay.direction.normalized;
            float t = (Vector3.Dot(m_n, m_a0) - Vector3.Dot(m_n, p0)) / Vector3.Dot(m_n, u);

            Vector3 p = p0 + t * u;  // p为手柄射线与平面(n,a)的相交点

            if (m_lassoPoints.Count == 0   // 如果手柄移动量足够大
                || Vector3.Distance(p, m_lassoPoints[m_lassoPoints.Count - 1]) > 0.3)
            {

                m_lassoRender.GetComponent<LineRenderer>().positionCount = m_lassoPoints.Count;
                m_lassoPoints.Add(p);
                m_lassoRender.GetComponent<LineRenderer>().SetPositions(
                    m_lassoPoints.ToArray());
            }
        }
    }

    private void DrawingFreeSketch()
    {
        // 处理鼠标释放事件（FreeSketch绘制结束）
        if (m_isFreeSketched &&
            (Controller.UPvr_GetKeyUp(0, Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonUp(0)))
        {
            m_isFreeSketched = false;

            m_tree.AddNewBranch(m_sketchPoints);
            UpdateTreeObjects();

            m_sketchPoints.Clear();
            m_SketchRender.GetComponent<LineRenderer>().positionCount = 0;
        }
        // 处理鼠标滑动事件（FreeSketch绘制中）
        if (m_isFreeSketched)
        {
            Vector3 p0 = m_ctrlRay.origin;
            Vector3 u = m_ctrlRay.direction.normalized;
            float t = (Vector3.Dot(m_n, m_a0) - Vector3.Dot(m_n, p0)) / Vector3.Dot(m_n, u);

            Vector3 p = p0 + t * u;  // p为手柄射线与平面(n,a)的相交点

            if (m_sketchPoints.Count == 0   // 如果手柄的位移量最够大
                || Vector3.Distance(p, m_sketchPoints[m_sketchPoints.Count - 1]) > 0.1)
            {
                // 将点加入到lineRenderer中
                m_SketchRender.GetComponent<LineRenderer>().positionCount = m_sketchPoints.Count;
                m_sketchPoints.Add(p);
                m_SketchRender.GetComponent<LineRenderer>().SetPositions(
                    m_sketchPoints.ToArray());
            }
        }
    }


    /***********【UI测试】，这个函数在Pico设备中没有响应***********/
    int t_count = 0;
    public void OnClick()
    {
        m_Text.text = "Click " + t_count.ToString();
        t_count++;
    }
}
