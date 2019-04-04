using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KdTree;
using System;

namespace TreeManager
{
    public class Bud       // 【芽】
    {
        public int m_type;   // 0 Terminal bud, 1 Lateral bud
        public Vector3 pos;  // 位置
        public Vector3 dir;  // 方向

        public Bud()
        {
            pos = new Vector3(0.0f, 0.0f, 0.0f);
            dir = new Vector3(0.0f, 0.0f, 0.0f);
            m_type = 0;
        }

        public Bud(int type, Vector3 p, Vector3 d) { m_type = type; pos = p; dir = d; }
    }

    public class Leaf      // 【树叶】
    {
        public Vector3 pos;  // 位置
        public Vector3 dir;  // 方向
        public Vector3 tan;  // （为了节省计算时间）切线方向
        public Vector3 norm; // （为了节省计算时间）法线方向
        public Leaf(Vector3 p, Vector3 d, Vector3 t, Vector3 n) { pos = p; dir = d; tan = t; norm = n; }
    }

    public class InterNode  // 【枝干的一个骨节】
    {
        public Vector3 a;  // 枝段起点
        public Vector3 b;  // 枝段终点

        public List<Bud> m_buds;           /// 该枝段拥有的芽
        public List<Leaf> m_leaves;        /// 该枝段拥有的树叶
        public List<InterNode> m_childs;   /// 该枝段拥有的后续枝段

        public float ra;   // 枝段半径（起点a位置处）
        public float rb;   // 枝段半径（终点b位置处）

        public int level;  //  该枝段按从顶向下的顺序所属的层次（即从树叶段向根节点的顺序）
        
        public bool m_isInterpolated;   // （无需关注）Tag: 是否已经被插值过了，用来禁止Tree::Interpolation()函数的修改

        public bool isSelected;         // （暂时未被使用）用来标记该枝段是否被手柄选中

        public InterNode()
        {
            m_buds = new List<Bud>();
            m_childs = new List<InterNode>();
            m_leaves = new List<Leaf>();

            ra = rb = 0.1f;

            m_isInterpolated = false;
            isSelected = false;
        }
    }

    public class Tree3D     //【重要】最为核心的算法类
    {
        InterNode m_root;                /// 根节点（根部枝端）!!!!!!
        List<Mesh> m_meshes;             /// 枝干部分拥有的Mesh
        List<Mesh> m_leafMeshes;         /// 树叶部分拥有的Mesh
        List<Mesh> m_selectedMeshes;     /// 被选中枝干部分拥有的Mesh（暂时没有使用）

        int m_faceCount;                /// （无需关注）每个枝端的三角面数

        public enum GrowthMode          /// 关于生长模式的枚举类型
        {
            _Free = 0,   // 自由生长
            _Lasso = 1,  // 使用套索
            _Brush = 2   // 使用刷子
        }

        public GrowthMode m_growthMode;      // 树的生长模式（_Free,_Lasso,_Brush）

        public List<float[]> m_KDmarkerPoints;    /// 空间中的MarkerPoints
        public List<int> m_KDnodes;               /// （无需关注）空间中的MarkerPoints对应的Index

        public KdTree<float, int> kdtree;    /// KD树

        System.Random m_random;   // 随机数生成器

        // properties
        private float m_occupancyRadius;     /// 每个芽的占有域半径
        private float m_perceptionRadius;    /// 每个芽的探测域半径
        private float m_interNodeLength;     /// 每个枝干的基础长度
        private float m_leafSize;            /// 叶片的尺寸
        private float m_branchBaseRadius;    /// 枝干的基础半径（在树的末端枝干处）
        private float m_branchRadiusFactor;  /// 枝干的半径增长率（从树的末端枝干处从上向下半径逐渐增大）

        private float m_gravityFactor;       /// 重力的影响因子

        // public-use
        public bool m_isUpdateMesh = false;  // 监听mesh是否被更新了，如果被更新则在Update()中更新Mesh
        public bool m_isUpdateMeshDone;      // 记录mesh是否被更新完毕（即外部线程是否可以终止）
        public bool m_isModified = false;    // 树的形态是否发生修改（这个值被用在基于Lasso生成树处，可见SubThreadLasso()函数）

        // 用于生成画刷路径的东西
        public List<Vector3> m_markerDirs;   // 记录画刷路径结点方向的东西
        public List<Vector3> m_markerNode;   // 记录画刷路径结点坐标的东西
        public List<float> m_markerRads;   // 记录画刷路径结点半径的东西

        /// <summary>
        /// 构造函数是也，用于初始化变量
        /// </summary>
        public Tree3D()  
        {
            m_random = new System.Random();

            m_meshes = new List<Mesh>();
            m_leafMeshes = new List<Mesh>();
            m_selectedMeshes = new List<Mesh>();
            m_markerDirs = new List<Vector3>();
            m_markerNode = new List<Vector3>();
            m_markerRads = new List<float>();

            m_root = new InterNode();

            m_faceCount = 5;

            SetupParameters(GrowthMode._Free);

            m_KDmarkerPoints = new List<float[]>();
            m_KDnodes = new List<int>();

            m_isUpdateMesh = false;
            m_isUpdateMeshDone = true;
            m_isModified = false;
        }

        /// <summary>
        /// 用来设置相关参数（对于不同的生长模式GrowthMode，有不同的参数）
        /// </summary>
        public void SetupParameters(GrowthMode mode = GrowthMode._Free)
        {
            m_growthMode = mode;

            if(m_growthMode == GrowthMode._Free)   // 自由生长模式下的参数
            {
                m_occupancyRadius = 0.5f;
                m_perceptionRadius = 1.0f;
                m_interNodeLength = 0.5f;
                m_leafSize = 0.20f;
                m_gravityFactor = 0.5f;

                m_branchBaseRadius = 0.01f;
                m_branchRadiusFactor = 1.04f;
            }
            if(m_growthMode == GrowthMode._Lasso)  // 使用Lasso模式下的参数
            {
                m_occupancyRadius = 0.3f;
                m_perceptionRadius = 0.7f;
                m_interNodeLength = 0.3f;
                m_leafSize = 0.15f;
                m_gravityFactor = 0.0f;

                m_branchBaseRadius = 0.01f;
                m_branchRadiusFactor = 1.03f;
            }
            if (m_growthMode == GrowthMode._Brush)  // 使用Lasso模式下的参数
            {
                m_occupancyRadius = 0.5f;
                m_perceptionRadius = 1.0f;
                m_interNodeLength = 0.5f;
                m_leafSize = 0.20f;
                m_gravityFactor = 0.0f;

                m_branchBaseRadius = 0.01f;
                m_branchRadiusFactor = 1.03f;
            }
        }


        /// <summary>
        /// 清除树的所有数据
        /// </summary>
        public void ClearAllMarkerPoints()
        {
            m_KDmarkerPoints.Clear();
            m_KDnodes.Clear();

            m_markerNode.Clear();
            m_markerRads.Clear();
            m_markerDirs.Clear();
        }
        public void ClearAllData()
        {
            if (kdtree != null)
                kdtree.Clear();

            foreach (Mesh m in m_meshes)    /// 这里仍然是不清楚C#是否会自动释放内存，因此保险起见手动释放
                m.Clear();
            foreach (Mesh m in m_leafMeshes)
                m.Clear();
            foreach (Mesh m in m_selectedMeshes)
                m.Clear();

            m_meshes.Clear();
            m_leafMeshes.Clear();
            m_selectedMeshes.Clear();

            Queue<InterNode> queue = new Queue<InterNode>();
            Stack<InterNode> stack = new Stack<InterNode>();
            queue.Enqueue(this.m_root);

            while(queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                stack.Push(cur);
                foreach (InterNode i in cur.m_childs)
                    queue.Enqueue(i);
            }

            foreach(InterNode cur in stack)
            {
                cur.m_childs.Clear();
                cur.m_buds.Clear();
            }

            InitTree();
        }

        /*******************初始化树，为树建立一个最初的芽********************************/
        public void InitTree()
        {
            InterNode node = new InterNode();

            node.a = new Vector3(0.0f, 0.0f, 0.0f);
            node.b = new Vector3(0.0f, 1.0f, 0.0f);

            node.m_buds.Add(new Bud(0, node.b, new Vector3(0.0f, 1.0f, 0.0f)));

            m_root = node;

        }

        /****************** 给树重新添加芽 ********************************************************/
        public void RecreateBuds()
        {
            if (m_root == null)  // 如果树为空
            {
                InitTree();
                return;
            }

            UpdateTreeLevels();

            Queue<InterNode> queue = new Queue<InterNode>();
            queue.Enqueue(this.m_root);

            while(queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                if(cur.level == 0)
                    cur.m_buds.Add(new Bud(0, cur.b, (cur.b - cur.a).normalized));  // 加一个顶芽
                if(cur.level <= 2)  // 新建一个侧芽
                {
                    float radio = m_random.Next(200, 800) / 1000.0f;  // 0.2 - 0.8
                    Vector3 budDir = GetOneNormalVectorFrom(cur.b - cur.a) * radio + (cur.b - cur.a).normalized * (1.0f - radio);
                    budDir = Quaternion.AngleAxis(360.0f * m_random.Next(0, 36000) / 36000.0f, (cur.b - cur.a).normalized) * budDir;
                    budDir.Normalize();

                    Bud newBud = new Bud(1, cur.b, budDir);

                    cur.m_buds.Add(newBud);
                }

                foreach (InterNode next in cur.m_childs)
                    queue.Enqueue(next);
            }
        }

        /***************** 【重要】 如果点击了Iterate For Once按钮，树进行一次生长迭代，并更新相关信息********************/
        public void btn_iterateOnce()
        {
            m_random = new System.Random();   /// 初始化随机数

            IterateOnce();   // 进行一轮迭代（根据Marker Points对当前所有的芽进行一次更新）

            Interpolation();  // 进行一次简单的插值，使得枝干相对光滑（这个函数后面会换成其他更好的插值算法）
            UpdateBranchRadius(); // 计算所有枝干的半径
            UpdateTreeLevels();   // 计算所有枝干的所属层次（自顶向下）
            UpdateLeaves();       // 生成树叶

            m_isUpdateMesh = true;   // 设置Mesh已被更新（Update()函数会捕获这一消息，从而重新显示新的植物）
        }

        /***************** 【重要】 对树进行一次迭代********************/
        public void IterateOnce()
        {
            Queue<InterNode> queue = new Queue<InterNode>();
            Queue<InterNode> terminalList = new Queue<InterNode>();       // 记录所有的顶芽
            Queue<InterNode> lateralList = new Queue<InterNode>();        // 记录所有的侧芽

            queue.Enqueue(this.m_root);

            while (queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                // 清除Bud的占有域空间
                Vector3 pos = cur.b;
                var killed = kdtree.RadialSearch(new float[3] { pos.x, pos.y, pos.z }, this.m_occupancyRadius);  // 要被Kill掉的点

                for (int k = 0; k < killed.Length; k++)
                    kdtree.RemoveAt(killed[k].Point);

                // 把芽放入待测序列中去
                foreach (Bud curBud in cur.m_buds) 
                {
                    if (curBud.m_type == 0)
                        terminalList.Enqueue(cur);
                    else
                        lateralList.Enqueue(cur);
                }

                foreach (InterNode i in cur.m_childs)
                    queue.Enqueue(i);
            }

            // 首先对顶芽进行处理
            foreach (InterNode cur in terminalList)
            {
                foreach (Bud curBud in cur.m_buds)
                    if (curBud.m_type == 0)
                        CreateOneMetamer(cur, curBud.dir);

                cur.m_buds.Clear();
            }
            // 然后对侧芽进行处理
            foreach (InterNode cur in lateralList)
            {
                foreach (Bud curBud in cur.m_buds)
                    if (curBud.m_type == 1)
                        CreateOneMetamer(cur, curBud.dir);

                cur.m_buds.Clear();
            }
        }

        /*******************对当前枝干curNode，根据其芽的方向baseDir，生成新的枝干********************************/
        public void CreateOneMetamer(InterNode curNode, Vector3 basedir)  // 从curNode.b沿着方向basedir生长
        {
            List<InterNode> newInterNodes = new List<InterNode>();   // 新生成的InterNodes;

            int MaxIterCount = 2;
            for (int i = 0; i < MaxIterCount; i++)   // 总共要生成MaxIterCount个新的枝干骨节，pape原文中这个值为3
            {
                Vector3 pos = new Vector3();   // 当前探索的中心位置
                Vector3 dir = new Vector3();   // 当前探索的方向

                if (i == 0) 
                {
                    pos = curNode.b;
                    dir = basedir.normalized;
                }
                else
                {
                    pos = newInterNodes[i - 1].b;
                    dir = (newInterNodes[i - 1].b - newInterNodes[i - 1].a).normalized;
                }

                if(m_markerDirs.Count != 0)   // 如果是在Brush模式下,检查是否位于边界
                {
                    bool m_isEdge = true;
                    for(int k=0; k<m_markerDirs.Count; ++k)
                    {
                        if ((m_markerNode[k] - pos).sqrMagnitude < 0.95f * m_markerRads[k] * m_markerRads[k])
                            m_isEdge = false;
                    }

                    if (m_isEdge)
                        break;
                }

                // 根据探测区域计算方向
                var percepted = kdtree.RadialSearch(new float[3] { pos.x, pos.y, pos.z }, m_perceptionRadius);  // 位于探测区域半径内的点

                if (percepted.Length == 0)  // 如果点数非常的少，跳出
                    break;

                Vector3 nextDir = new Vector3(0.0f, 0.0f, 0.0f);  // 新的枝干的方向
                bool[] isRemoved = new bool[percepted.Length];
                bool m_isRestUsed = false; // 是否有点被使用了

                for (int k = 0; k < percepted.Length; k++)
                {
                    Vector3 _dir = (new Vector3(percepted[k].Point[0], percepted[k].Point[1], percepted[k].Point[2])) - pos;
                    _dir.Normalize();

                    if (Vector3.Dot(_dir, dir) > 0.3f)  // 如果角度合适，则征用该marker
                    {
                        nextDir += _dir;
                        if (m_growthMode == GrowthMode._Brush)  // 如果是Brush模式还要加上这个方向
                            nextDir += 0.3f * m_markerDirs[percepted[k].Value];

                        isRemoved[k] = true;
                        m_isRestUsed = true;
                    }
                    else
                        isRemoved[k] = false;
                }
                if (!m_isRestUsed)  // 如果一个点也没有被用到
                    break;

                nextDir.Normalize();
                nextDir = nextDir + m_gravityFactor * new Vector3(0.0f, 1.0f, 0.0f);
                nextDir.Normalize();

                for (int k = 0; k < percepted.Length; k++)
                {
                    if (isRemoved[k])
                        kdtree.RemoveAt(percepted[k].Point);
                }

                // 生成下一段InterNode
                InterNode next = new InterNode();
                next.a = pos;
                next.b = pos + m_interNodeLength * nextDir;   //[更新]

                // 为这段新的InterNode添加新的Bud;
                float radio = m_random.Next(200, 800) / 1000.0f;  // 0.2 - 0.8
                Vector3 budDir = GetOneNormalVectorFrom(next.b - next.a) * radio + (next.b - next.a).normalized * (1.0f - radio);
                budDir = Quaternion.AngleAxis(360.0f * m_random.Next(0, 36000) / 36000.0f, (next.b - next.a).normalized) * budDir;
                budDir.Normalize();

                Bud newBud = new Bud(1, next.b, budDir);

                next.m_buds.Add(newBud);

                if (i == MaxIterCount - 1)
                    next.m_buds.Add(new Bud(0, next.b, (next.b - next.a).normalized));

                newInterNodes.Add(next);
            }

            if (newInterNodes.Count != 0)
            {
                curNode.m_childs.Add(newInterNodes[0]);

                for (int i = 0; i < newInterNodes.Count - 1; i++)
                    newInterNodes[i].m_childs.Add(newInterNodes[i + 1]);

                this.m_isModified = true;
            }
        }

        public void RecreateMarkerPoints()
        {
            this.m_KDmarkerPoints.Clear();
            this.m_KDnodes.Clear();

            Queue<InterNode> queue = new Queue<InterNode>();
            queue.Enqueue(this.m_root);

            List<Bud> buds = new List<Bud>();

            while (queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                foreach (Bud b in cur.m_buds)
                    buds.Add(b);

                foreach (InterNode c in cur.m_childs)
                    queue.Enqueue(c);
            }

            // Generate marker points with each bud in sequencial

            int maxMarkerCount = 20000;
            int eachMarkerCount = 200;
            if (buds.Count * eachMarkerCount > maxMarkerCount)
                eachMarkerCount = maxMarkerCount / buds.Count;

            foreach (Bud bud in buds)
            {
                for (int i = 0; i < eachMarkerCount; i++)
                {
                    float[] pts = new float[3];

                    pts[0] = bud.pos.x + 1.5f * m_perceptionRadius * m_random.Next(-100000000, 100000000) / 100000000.0f;
                    pts[1] = bud.pos.y + 1.5f * m_perceptionRadius * m_random.Next(-100000000, 100000000) / 100000000.0f;
                    pts[2] = bud.pos.z + 1.5f * m_perceptionRadius * m_random.Next(-100000000, 100000000) / 100000000.0f;
                    m_KDmarkerPoints.Add(pts);

                    m_KDnodes.Add(0);
                }
            }

            if (kdtree != null)
                kdtree.Clear();
            kdtree = BuildKDTree();
        }

        public void RecreateMarkerPointByLasso(List<Vector3> lasso, Vector3 dir, Vector3 norm)
        {
            this.m_KDmarkerPoints.Clear();
            this.m_KDnodes.Clear();

            // 构造lasso的平面映射
            Vector2[] lasso2D = new Vector2[lasso.Count];
            int minx = int.MaxValue;
            int miny = int.MaxValue;
            int maxx = int.MinValue;
            int maxy = int.MinValue;

            for(int i=0; i<lasso.Count; i++)
            {
                float _x = (float)Math.Sqrt(lasso[i].x * lasso[i].x + lasso[i].z * lasso[i].z);

                if (Vector3.Dot(lasso[i], dir) >= 0)
                    lasso2D[i] = new Vector2(_x, lasso[i].y);
                else
                    lasso2D[i] = new Vector2(-_x, lasso[i].y);

                if (lasso2D[i].x < minx)
                    minx = (int)lasso2D[i].x;
                if (lasso2D[i].x > maxx)
                    maxx = (int)lasso2D[i].x;
                if (lasso2D[i].y < miny)
                    miny = (int)lasso2D[i].y;
                if (lasso2D[i].y > maxy)
                    maxy = (int)lasso2D[i].y;
            }

            int deltaZ = (maxy - miny)/4;

            for(int i=0; i<20000;)
            {
                float[] pts = new float[3];

                float x = (float)m_random.Next(minx * 100000000, maxx * 100000000) / 100000000.0f;
                float y = (float)m_random.Next(miny * 100000000, maxy * 100000000) / 100000000.0f;


                if (!IsPointInPolygon(new Vector2(x, y), lasso2D))
                    continue;
                Vector3 t = x * dir;

                float z = (float)m_random.Next(-deltaZ*10000,deltaZ*10000)/10000.0f;
                t = t + z * norm;

                pts[0] = t.x;
                pts[1] = y;
                pts[2] = t.z;
                m_KDmarkerPoints.Add(pts);
                m_KDnodes.Add(0);

                i++;
            }

            if(kdtree != null)
                kdtree.Clear();
            kdtree = BuildKDTree();
        }

        public List<float[]> UpdateMarkerPointsByBrush(Vector3 pt0, Vector3 pt,float radius = 3.0f)   // 根据当前情况生成一组新的markerPointa
        {
            m_markerDirs.Add((pt - pt0).normalized);  // 加入新的方向
            m_markerNode.Add(pt);
            m_markerRads.Add(radius);

            List<float[]> res = new List<float[]>();

            for(int i=0; i<400; i++)
            {
                float[] tmp = new float[3];

                tmp[0] = pt.x + radius * m_random.Next(-1000000000, 1000000000) / 1000000000.0f;
                tmp[1] = pt.y + radius * m_random.Next(-1000000000, 1000000000) / 1000000000.0f;
                tmp[2] = pt.z + radius * m_random.Next(-1000000000, 1000000000) / 1000000000.0f;
                m_KDmarkerPoints.Add(tmp);

                m_KDnodes.Add(m_markerDirs.Count-1);
                res.Add(tmp);
            }
            
            return res;
        }

        public KdTree<float, int> BuildKDTree()
        {
            var _kdtree = new KdTree<float, int>(3, new KdTree.Math.FloatMath());

            for (int i = 0; i < m_KDmarkerPoints.Count; i++)
            {
                _kdtree.Add(new[] { m_KDmarkerPoints[i][0], m_KDmarkerPoints[i][1], m_KDmarkerPoints[i][2] }, m_KDnodes[i]);
            }

            return _kdtree;
        }

        // 生成mesh
        public List<Mesh> GetMesh()
        {
            // clear former data
            foreach (Mesh m in m_meshes)
                m.Clear();
            m_meshes.Clear();

            // 首先生成tree meshes和leaf meshes;
            Queue<InterNode> queue = new Queue<InterNode>(); // for traverse

            List<InterNode> parent = new List<InterNode>();   // parents;
            List<InterNode> list = new List<InterNode>();     // curnodes;
            queue.Enqueue(this.m_root);

            parent.Add(null);
            list.Add(this.m_root);

            while (queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                for (int i = 0; i < cur.m_childs.Count; i++)
                {
                    list.Add(cur.m_childs[i]);
                    parent.Add(cur);
                    queue.Enqueue(cur.m_childs[i]);
                }
            }

            List<List<Vector3>> vecs = new List<List<Vector3>>();
            List<List<Vector2>> uvs = new List<List<Vector2>>();
            List<List<Vector3>> norms = new List<List<Vector3>>();
            List<List<int>> indices = new List<List<int>>();
            int count = 0;

            int mId = 0;  // current tree mesh ID

            for (int m = 0; m < list.Count; m++)
            {
                if (count == 0)
                {
                    m_meshes.Add(new Mesh());
                    vecs.Add(new List<Vector3>());
                    uvs.Add(new List<Vector2>());
                    norms.Add(new List<Vector3>());
                    indices.Add(new List<int>());
                }

                InterNode cur = list[m];

                Vector3 dir = (cur.b - cur.a).normalized;  // 末端
                Vector3 dir1 = dir;  // 前端

                if (m != 0)
                    dir1 = (parent[m].b - parent[m].a).normalized;

                Vector3 norm = GetOneNormalVectorFrom(dir);
                Vector3 norm1 = GetOneNormalVectorFrom(dir1);
                if (Vector3.Dot(norm, norm1) < 0)
                    norm1 = -norm1;

                Vector3[] topPts = new Vector3[m_faceCount];
                Vector3[] botPts = new Vector3[m_faceCount];
                Vector3[] faceNorms = new Vector3[m_faceCount];

                // 首先生成相应的点
                for (int i = 0; i < m_faceCount; i++)
                {
                    Vector3 t_norm = Quaternion.AngleAxis(i / 6.0f * 360.0f, dir) * norm;
                    Vector3 t_norm1 = Quaternion.AngleAxis(i / 6.0f * 360.0f, dir1) * norm1;
                    topPts[i] = cur.b + cur.rb * t_norm;
                    botPts[i] = cur.a + cur.ra * t_norm1;

                    faceNorms[i] = t_norm.normalized;
                }

                // 接着生成面片
                for (int i = 0; i < m_faceCount; i++)
                {
                    int id1 = i;
                    int id2 = (i + 1) % m_faceCount;

                    vecs[mId].Add(botPts[id2]); vecs[mId].Add(topPts[id2]); vecs[mId].Add(topPts[id1]);
                    vecs[mId].Add(topPts[id1]); vecs[mId].Add(botPts[id1]); vecs[mId].Add(botPts[id2]);

                    norms[mId].Add(faceNorms[id2]); norms[mId].Add(faceNorms[id2]); norms[mId].Add(faceNorms[id1]);
                    norms[mId].Add(faceNorms[id1]); norms[mId].Add(faceNorms[id1]); norms[mId].Add(faceNorms[id2]);

                    indices[mId].Add(count + 0); indices[mId].Add(count + 1); indices[mId].Add(count + 2);
                    indices[mId].Add(count + 3); indices[mId].Add(count + 4); indices[mId].Add(count + 5);

                    uvs[mId].Add(new Vector2(id2 % m_faceCount, 0.0f));
                    uvs[mId].Add(new Vector2(id2 % m_faceCount, 1.0f));
                    uvs[mId].Add(new Vector2(id1 % m_faceCount, 1.0f));
                    uvs[mId].Add(new Vector2(id1 % m_faceCount, 1.0f));
                    uvs[mId].Add(new Vector2(id1 % m_faceCount, 0.0f));
                    uvs[mId].Add(new Vector2(id2 % m_faceCount, 0.0f));

                    count += 6;
                }

                if (vecs[mId].Count >= 60000)
                {
                    m_meshes[mId].vertices = vecs[mId].ToArray();
                    m_meshes[mId].triangles = indices[mId].ToArray();
                    m_meshes[mId].normals = norms[mId].ToArray();
                    m_meshes[mId].uv = uvs[mId].ToArray();

                    mId++;
                    count = 0;
                }
            }
            if (vecs[mId].Count != 0)
            {
                m_meshes[mId].vertices = vecs[mId].ToArray();
                m_meshes[mId].triangles = indices[mId].ToArray();
                m_meshes[mId].normals = norms[mId].ToArray();
                m_meshes[mId].uv = uvs[mId].ToArray();
            }
            if (m_meshes.Count == 0)
                m_meshes.Add(new Mesh());

            //Debug.Log("Indices count:" + indices.Count.ToString());

            return this.m_meshes;
        }

        private void Interpolation()
        {
            // 每次Interpolation是对InterNode的half-interpolate
            Queue<InterNode> queue = new Queue<InterNode>(); // for traverse

            List<InterNode> parent = new List<InterNode>();   // parents;
            List<int> cid = new List<int>();  // curnodes'index of parent'm_childs;
            List<InterNode> list = new List<InterNode>();     // curnodes;
            queue.Enqueue(this.m_root);

            while (queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                for (int i = 0; i < cur.m_childs.Count; i++)
                {
                    list.Add(cur.m_childs[i]);
                    cid.Add(i);
                    parent.Add(cur);
                    queue.Enqueue(cur.m_childs[i]);
                }
            }

            // Interpolate
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].m_isInterpolated)
                    continue;

                InterNode cur = list[i];
                InterNode par = parent[i];

                Vector3 v1 = cur.a;
                Vector3 v2 = cur.b;
                Vector3 d1 = (par.b - par.a).normalized;
                Vector3 d2 = (cur.b - cur.a).normalized;
                float halflen = 0.5f * (v2 - v1).sqrMagnitude;

                Vector3 newPt = v1 + 0.5f * (0.6f * halflen * d2 + 0.4f * halflen * d1);

                InterNode newInterNode = new InterNode();   // 前半段 
                newInterNode.a = cur.a;
                newInterNode.b = newPt;
                newInterNode.m_childs.Add(cur);
                newInterNode.m_isInterpolated = true;

                cur.a = newPt;
                cur.m_isInterpolated = true;

                // insert new internode into current tree structure;
                par.m_childs.RemoveAt(cid[i]);
                par.m_childs.Insert(cid[i], newInterNode);
            }
        }

        public void UpdateBranchRadius()
        {
            Queue<InterNode> queue = new Queue<InterNode>();
            Stack<InterNode> stack = new Stack<InterNode>();

            queue.Enqueue(this.m_root);

            while (queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                stack.Push(cur);

                foreach (InterNode i in cur.m_childs)
                    queue.Enqueue(i);
            }

            while (stack.Count != 0)
            {
                InterNode cur = stack.Pop();
                //Debug.Log("ChildCount: " + cur.m_childs.Count.ToString());

                if (cur.m_childs.Count == 0)
                {
                    cur.rb = 0.001f;
                    cur.ra = m_branchBaseRadius*m_branchRadiusFactor;
                }
                else if (cur.m_childs.Count == 1)
                {
                    cur.rb = cur.m_childs[0].ra;
                    cur.ra = cur.rb*m_branchRadiusFactor;
                }
                else
                {

                    float radius = 0;
                    foreach (InterNode c in cur.m_childs)
                    {
                        radius += c.rb * c.rb;
                    }

                    cur.rb = Mathf.Sqrt(radius);
                    cur.ra = m_branchRadiusFactor * cur.rb;
                }

                if (cur.ra >= 0.4f) cur.ra = 0.4f;
                if (cur.rb >= 0.4f) cur.rb = 0.4f;
            }
        }

        public void UpdateTreeLevels()
        {
            Queue<InterNode> queue = new Queue<InterNode>();
            Stack<InterNode> stack = new Stack<InterNode>();

            queue.Enqueue(this.m_root);

            while (queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                stack.Push(cur);

                foreach (InterNode i in cur.m_childs)
                    queue.Enqueue(i);
            }

            while (stack.Count != 0)
            {
                InterNode cur = stack.Pop();

                if (cur.m_childs.Count == 0)
                    cur.level = 1;
                else
                {
                    cur.level = int.MinValue;

                    foreach(InterNode c in cur.m_childs)
                    {
                        if (cur.level < c.level + 1) 
                            cur.level = c.level + 1; 
                    }
                }
            }
        }

        public void UpdateLeaves()
        {
            Queue<InterNode> queue = new Queue<InterNode>();
            queue.Enqueue(this.m_root);

            while (queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                cur.m_leaves.Clear();

                if(cur.level <= 10)  // 5
                {
                    for(int i=0; i<5; i++)  //10
                    {
                        float ratio = (float)m_random.Next(1000, 10000) / 10000.0f;

                        Vector3 tdir = (cur.b - cur.a).normalized;
                        Vector3 tnorm = GetOneNormalVectorFrom(tdir);
                        tnorm = Quaternion.AngleAxis(360.0f * (float)m_random.Next(0, 36000) / 36000.0f,tdir)*tnorm;
                        Vector3 pos = (cur.a + ratio*(cur.b-cur.a)) + cur.rb * tnorm;
                        Vector3 dir = (0.5f * tnorm + 0.5f * tdir).normalized;
                        Vector3 tan = Vector3.Cross(dir, tdir).normalized;
                        cur.m_leaves.Add(new Leaf(pos, dir, tan, Vector3.Cross(dir, tan).normalized));
                    }
                }
                
                foreach (InterNode i in cur.m_childs)
                    queue.Enqueue(i);
            }
        }

        public Vector3 GetOneNormalVectorFrom(Vector3 dir)
        {
            if (dir.x == 0)
                return new Vector3(0, 0, -1);
            else
                return new Vector3(-dir.z / dir.x, 0, 1).normalized;
        }

        public List<Vector3> GetBudLocationList()
        {
            List<Vector3> positions = new List<Vector3>();
            Queue<InterNode> queue = new Queue<InterNode>();

            queue.Enqueue(this.m_root);

            while (queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                foreach (Bud b in cur.m_buds)
                    positions.Add(b.pos);

                foreach (InterNode i in cur.m_childs)
                    queue.Enqueue(i);
            }

            return positions;
        }

        public InterNode GetHitInfo(Vector3 hitpos)  // Get the hit internode by 'hitpos'
        {
            Queue<InterNode> queue = new Queue<InterNode>();
            queue.Enqueue(this.m_root);
            float distance = float.MaxValue;
            InterNode tmp = null;

            while (queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                // to do : check which internode is the most nearest from the mouse ray;
                if (distance > Vector3.Distance(hitpos, cur.a))
                {
                    tmp = cur;
                    distance = Vector3.Distance(hitpos, cur.a);
                }

                foreach (InterNode i in cur.m_childs)
                    queue.Enqueue(i);
            }

            return tmp;
        }

        public List<Mesh> GetLeafMesh()
        {
            foreach (Mesh m in m_leafMeshes)
                m.Clear();
            m_leafMeshes.Clear();

            Queue<InterNode> queue = new Queue<InterNode>();
            queue.Enqueue(this.m_root);

            int count = 0;
            int mid = 0;

            List<List<Vector3>> vecs = new List<List<Vector3>>();
            List<List<Vector2>> uvs = new List<List<Vector2>>();
            List<List<Vector3>> norms = new List<List<Vector3>>();
            List<List<int>> indices = new List<List<int>>();

            while (queue.Count != 0 )
            {
                InterNode cur = queue.Dequeue();

                foreach (InterNode i in cur.m_childs)
                    queue.Enqueue(i);

                if (count == 0)
                {
                    m_leafMeshes.Add(new Mesh());
                    vecs.Add(new List<Vector3>());
                    uvs.Add(new List<Vector2>());
                    norms.Add(new List<Vector3>());
                    indices.Add(new List<int>());
                }
                
                foreach(Leaf lf in cur.m_leaves)
                {
                    Vector3 v1 = lf.pos + m_leafSize * lf.tan;
                    Vector3 v2 = v1 + 2 * m_leafSize * lf.dir;
                    Vector3 v3 = lf.pos - m_leafSize * lf.tan;
                    Vector3 v4 = v3 + 2 * m_leafSize * lf.dir;
                    vecs[mid].Add(v1); vecs[mid].Add(v2); vecs[mid].Add(v4);
                    vecs[mid].Add(v4); vecs[mid].Add(v3); vecs[mid].Add(v1);

                    norms[mid].Add(lf.norm); norms[mid].Add(lf.norm); norms[mid].Add(lf.norm);
                    norms[mid].Add(lf.norm); norms[mid].Add(lf.norm); norms[mid].Add(lf.norm);

                    uvs[mid].Add(new Vector2(0.0f, 0.0f)); uvs[mid].Add(new Vector2(0.0f, 1.0f));
                    uvs[mid].Add(new Vector2(1.0f, 1.0f)); uvs[mid].Add(new Vector2(1.0f, 1.0f));
                    uvs[mid].Add(new Vector2(1.0f, 0.0f)); uvs[mid].Add(new Vector2(0.0f, 0.0f));

                    indices[mid].Add(count + 0); indices[mid].Add(count + 1);indices[mid].Add(count + 2);
                    indices[mid].Add(count + 3); indices[mid].Add(count + 4); indices[mid].Add(count + 5);

                    count += 6;
                }

                if(count >=60000)
                {
                    m_leafMeshes[mid].vertices = vecs[mid].ToArray();
                    m_leafMeshes[mid].normals = norms[mid].ToArray();
                    m_leafMeshes[mid].uv = uvs[mid].ToArray();
                    m_leafMeshes[mid].triangles = indices[mid].ToArray();

                    count = 0;
                    mid++;
                }
            }

            if (mid == 0)
            {
                m_leafMeshes[mid].vertices = vecs[mid].ToArray();
                m_leafMeshes[mid].normals = norms[mid].ToArray();
                m_leafMeshes[mid].uv = uvs[mid].ToArray();
                m_leafMeshes[mid].triangles = indices[mid].ToArray();
            }

            return m_leafMeshes;
        }

        public void UpdateBranchLength()
        {
            Queue<InterNode> queue = new Queue<InterNode>();
            queue.Enqueue(this.m_root);

            while(queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();
                Vector3 dir = cur.b - cur.a;
                cur.b = cur.a + 1.5f * dir;

                foreach(Bud bud in cur.m_buds)
                    bud.pos = cur.b;

                foreach(InterNode next in cur.m_childs)
                {
                    next.a = cur.b;

                    queue.Enqueue(next);
                }
            }
        }

        public InterNode GetHitInterNode(Vector3 hitPos)
        {
            Queue<InterNode> queue = new Queue<InterNode>();
            queue.Enqueue(this.m_root);

            float mindist = float.MaxValue;
            InterNode hitInterNode = null;

            while(queue.Count != 0)
            {
                InterNode cur = queue.Dequeue();

                float dist = Vector3.Distance(cur.a, hitPos);

                if(dist < mindist)
                {
                    mindist = dist;
                    hitInterNode = cur;
                }

                foreach (InterNode next in cur.m_childs)
                    queue.Enqueue(next);
            }
            return hitInterNode;
        }

        public void AddNewBranch(List<Vector3> points)
        {
            if (points.Count < 2)
                return;

            InterNode cur = GetHitInterNode(points[0]);  // 根据首个点的坐标获取到点击的internode

            if (cur == null)
                return;

            for(int i=0; i<points.Count-1; i++)
            {
                InterNode tmp = new InterNode();
                tmp.a = points[i];
                tmp.b = points[i + 1];

                cur.m_childs.Add(tmp);

                cur = tmp;
            }

            UpdateBranchRadius();
            UpdateTreeLevels();
            UpdateLeaves();
        }

        public bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
        {
            int polygonLength = polygon.Length, i = 0;
            bool inside = false;


            float pointX = point.x, pointY = point.y;


            float startX, startY, endX, endY;
            Vector2 endPoint = polygon[polygonLength - 1];
            endX = endPoint.x;
            endY = endPoint.y;
            while (i < polygonLength)
            {
                startX = endX; startY = endY; endPoint = polygon[i++]; endX = endPoint.x; endY = endPoint.y;
                inside ^= (endY > pointY ^ startY > pointY)&&
                    ((pointX - endX) < (pointY - endY) * (startX - endX) / (startY - endY));
            }
            return inside;
        }
    }
}