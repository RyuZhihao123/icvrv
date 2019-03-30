using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace KdTree
{
	public enum AddDuplicateBehavior
	{
		Skip,
		Error,
		Update
	}

	public class DuplicateNodeError : Exception
	{
		public DuplicateNodeError()
			: base("无法添加结点")
		{
		}
	}

	[Serializable]
	public class KdTree<TKey, TValue> : IKdTree<TKey, TValue>
	{
		public KdTree(int dimensions, ITypeMath<TKey> typeMath)
		{
			this.dimensions = dimensions;
			this.typeMath = typeMath;
			Count = 0;
		}

		public KdTree(int dimensions, ITypeMath<TKey> typeMath, AddDuplicateBehavior addDuplicateBehavior)
			: this(dimensions, typeMath)
		{
			AddDuplicateBehavior = addDuplicateBehavior;
		}

		private int dimensions;

		private ITypeMath<TKey> typeMath = null;

		private KdTreeNode<TKey, TValue> root = null;

		public AddDuplicateBehavior AddDuplicateBehavior { get; private set; }

		public bool Add(TKey[] point, TValue value)
		{
			var nodeToAdd = new KdTreeNode<TKey, TValue>(point, value);

			if (root == null)
			{
				root = new KdTreeNode<TKey, TValue>(point, value);
			}
			else
			{
				int dimension = -1;
				KdTreeNode<TKey, TValue> parent = root;

				do
				{
					// 增加搜索dimension
					dimension = (dimension + 1) % dimensions;

					//检测node是否是要加入到HyperSphere中的那个Node
					if (typeMath.AreEqual(point, parent.Point))
					{
						switch (AddDuplicateBehavior)
						{
							case AddDuplicateBehavior.Skip:
								return false;

							case AddDuplicateBehavior.Error:
								throw new DuplicateNodeError();

							case AddDuplicateBehavior.Update:
								parent.Value = value;
								return true;

							default:
								throw new Exception("Unexpected AddDuplicateBehavior");
						}
					}

					int compare = typeMath.Compare(point[dimension], parent.Point[dimension]);

					if (parent[compare] == null)
					{
						parent[compare] = nodeToAdd;
						break;
					}
					else
					{
						parent = parent[compare];
					}
				}
				while (true);
			}

			Count++;
			return true;
		}

		private void ReaddChildNodes(KdTreeNode<TKey, TValue> removedNode)
		{
			if (removedNode.IsLeaf)
				return;

			var nodesToReadd = new Queue<KdTreeNode<TKey, TValue>>();

			var nodesToReaddQueue = new Queue<KdTreeNode<TKey, TValue>>();

			if (removedNode.LeftChild != null)
				nodesToReaddQueue.Enqueue(removedNode.LeftChild);

			if (removedNode.RightChild != null)
				nodesToReaddQueue.Enqueue(removedNode.RightChild);

			while (nodesToReaddQueue.Count > 0)
			{
				var nodeToReadd = nodesToReaddQueue.Dequeue();

				nodesToReadd.Enqueue(nodeToReadd);

				for (int side = -1; side <= 1; side += 2)
				{
					if (nodeToReadd[side] != null)
					{
						nodesToReaddQueue.Enqueue(nodeToReadd[side]);

						nodeToReadd[side] = null;
					}
				}
			}

			while (nodesToReadd.Count > 0)
			{
				var nodeToReadd = nodesToReadd.Dequeue();

				Count--;
				Add(nodeToReadd.Point, nodeToReadd.Value);
			}
		}

		public void RemoveAt(TKey[] point)
		{
			if (root == null)
				return;

			KdTreeNode<TKey, TValue> node;

			if (typeMath.AreEqual(point, root.Point))
			{
				node = root;
				root = null;
				Count--;
				ReaddChildNodes(node);
				return;
			}

			node = root;

			int dimension = -1;
			do
			{
				dimension = (dimension + 1) % dimensions;

				int compare = typeMath.Compare(point[dimension], node.Point[dimension]);

				if (node[compare] == null)
					return;

				if (typeMath.AreEqual(point, node[compare].Point))
				{
					var nodeToRemove = node[compare];
					node[compare] = null;
					Count--;

					ReaddChildNodes(nodeToRemove);
				}
				else
					node = node[compare];
			}
			while (node != null);
		}

		public KdTreeNode<TKey, TValue>[] GetNearestNeighbours(TKey[] point, int count)
		{
			if (count > Count)
				count = Count;

			if (count < 0)
			{
				throw new ArgumentException("Number of neighbors cannot be negative");
			}

			if (count == 0)
				return new KdTreeNode<TKey, TValue>[0];

			var neighbours = new KdTreeNode<TKey, TValue>[count];

			var nearestNeighbours = new NearestNeighbourList<KdTreeNode<TKey, TValue>, TKey>(count, typeMath);

			var rect = HyperRect<TKey>.Infinite(dimensions, typeMath);

			AddNearestNeighbours(root, point, rect, 0, nearestNeighbours, typeMath.MaxValue);

			count = nearestNeighbours.Count;

			var neighbourArray = new KdTreeNode<TKey, TValue>[count];

			for (var index = 0; index < count; index++)
				neighbourArray[count - index - 1] = nearestNeighbours.RemoveFurtherest();

			return neighbourArray;
		}

		private void AddNearestNeighbours(
			KdTreeNode<TKey, TValue> node,
			TKey[] target,
			HyperRect<TKey> rect,
			int depth,
			NearestNeighbourList<KdTreeNode<TKey, TValue>, TKey> nearestNeighbours,
			TKey maxSearchRadiusSquared)
		{
			if (node == null)
				return;

			// 当前dimension
			int dimension = depth % dimensions;

			// 分割HyperRect成两个sub rectangles
			var leftRect = rect.Clone();
			leftRect.MaxPoint[dimension] = node.Point[dimension];

			var rightRect = rect.Clone();
			rightRect.MinPoint[dimension] = node.Point[dimension];

			int compare = typeMath.Compare(target[dimension], node.Point[dimension]);

			var nearerRect = compare <= 0 ? leftRect : rightRect;
			var furtherRect = compare <= 0 ? rightRect : leftRect;

			var nearerNode = compare <= 0 ? node.LeftChild : node.RightChild;
			var furtherNode = compare <= 0 ? node.RightChild : node.LeftChild;

			// 搜索最近的分支
			if (nearerNode != null)
			{
				AddNearestNeighbours(
					nearerNode,
					target,
					nearerRect,
					depth + 1,
					nearestNeighbours,
					maxSearchRadiusSquared);
			}

			TKey distanceSquaredToTarget;

			TKey[] closestPointInFurtherRect = furtherRect.GetClosestPoint(target, typeMath);
			distanceSquaredToTarget = typeMath.DistanceSquaredBetweenPoints(closestPointInFurtherRect, target);

			if (typeMath.Compare(distanceSquaredToTarget, maxSearchRadiusSquared) <= 0)
			{
				if (nearestNeighbours.IsCapacityReached)
				{
					if (typeMath.Compare(distanceSquaredToTarget, nearestNeighbours.GetFurtherestDistance()) < 0)
						AddNearestNeighbours(
							furtherNode,
							target,
							furtherRect,
							depth + 1,
							nearestNeighbours,
							maxSearchRadiusSquared);
				}
				else
				{
					AddNearestNeighbours(
						furtherNode,
						target,
						furtherRect,
						depth + 1,
						nearestNeighbours,
						maxSearchRadiusSquared);
				}
			}

			// 尝试将当前结点加入到最近的分支list中
			distanceSquaredToTarget = typeMath.DistanceSquaredBetweenPoints(node.Point, target);

			if (typeMath.Compare(distanceSquaredToTarget, maxSearchRadiusSquared) <= 0)
				nearestNeighbours.Add(node, distanceSquaredToTarget);
		}

		public KdTreeNode<TKey, TValue>[] RadialSearch(TKey[] center, TKey radius)
		{
			var nearestNeighbours = new NearestNeighbourList<KdTreeNode<TKey, TValue>, TKey>(typeMath);
			return RadialSearch(center, radius, nearestNeighbours);
		}

		public KdTreeNode<TKey, TValue>[] RadialSearch(TKey[] center, TKey radius, int count)
		{
			var nearestNeighbours = new NearestNeighbourList<KdTreeNode<TKey, TValue>, TKey>(count, typeMath);
			return RadialSearch(center, radius, nearestNeighbours);
		}

		private KdTreeNode<TKey, TValue>[] RadialSearch(TKey[] center, TKey radius, NearestNeighbourList<KdTreeNode<TKey, TValue>, TKey> nearestNeighbours)
		{
			AddNearestNeighbours(
				root,
				center,
				HyperRect<TKey>.Infinite(dimensions, typeMath),
				0,
				nearestNeighbours,
				typeMath.Multiply(radius, radius));

			var count = nearestNeighbours.Count;

			var neighbourArray = new KdTreeNode<TKey, TValue>[count];

			for (var index = 0; index < count; index++)
				neighbourArray[count - index - 1] = nearestNeighbours.RemoveFurtherest();

			return neighbourArray;
		}

		public int Count { get; private set; }

		public bool TryFindValueAt(TKey[] point, out TValue value)
		{
			var parent = root;
			int dimension = -1;
			do
			{
				if (parent == null)
				{
					value = default(TValue);
					return false;
				}
				else if (typeMath.AreEqual(point, parent.Point))
				{
					value = parent.Value;
					return true;
				}

				// Keep searching
				dimension = (dimension + 1) % dimensions;
				int compare = typeMath.Compare(point[dimension], parent.Point[dimension]);
				parent = parent[compare];
			}
			while (true);
		}

		public TValue FindValueAt(TKey[] point)
		{
			if (TryFindValueAt(point, out TValue value))
				return value;
			else
				return default(TValue);
		}

		public bool TryFindValue(TValue value, out TKey[] point)
		{
			if (root == null)
			{
				point = null;
				return false;
			}

			var nodesToSearch = new Queue<KdTreeNode<TKey, TValue>>();

			nodesToSearch.Enqueue(root);

			while (nodesToSearch.Count > 0)
			{
				var nodeToSearch = nodesToSearch.Dequeue();

				if (nodeToSearch.Value.Equals(value))
				{
					point = nodeToSearch.Point;
					return true;
				}
				else
				{
					for (int side = -1; side <= 1; side += 2)
					{
						var childNode = nodeToSearch[side];

						if (childNode != null)
							nodesToSearch.Enqueue(childNode);
					}
				}
			}

			point = null;
			return false;
		}

		public TKey[] FindValue(TValue value)
		{
			if (TryFindValue(value, out TKey[] point))
				return point;
			else
				return null;
		}

		private void AddNodeToStringBuilder(KdTreeNode<TKey, TValue> node, StringBuilder sb, int depth)
		{
			sb.AppendLine(node.ToString());

			for (var side = -1; side <= 1; side += 2)
			{
				for (var index = 0; index <= depth; index++)
					sb.Append("\t");

				sb.Append(side == -1 ? "L " : "R ");

				if (node[side] == null)
					sb.AppendLine("");
				else
					AddNodeToStringBuilder(node[side], sb, depth + 1);
			}
		}

		public override string ToString()
		{
			if (root == null)
				return "";

			var sb = new StringBuilder();
			AddNodeToStringBuilder(root, sb, 0);
			return sb.ToString();
		}

		private void AddNodesToList(KdTreeNode<TKey, TValue> node, List<KdTreeNode<TKey, TValue>> nodes)
		{
			if (node == null)
				return;

			nodes.Add(node);

			for (var side = -1; side <= 1; side += 2)
			{
				if (node[side] != null)
				{
					AddNodesToList(node[side], nodes);
					node[side] = null;
				}
			}
		}

		private void SortNodesArray(KdTreeNode<TKey, TValue>[] nodes, int byDimension, int fromIndex, int toIndex)
		{
			for (var index = fromIndex + 1; index <= toIndex; index++)
			{
				var newIndex = index;

				while (true)
				{
					var a = nodes[newIndex - 1];
					var b = nodes[newIndex];
					if (typeMath.Compare(b.Point[byDimension], a.Point[byDimension]) < 0)
					{
						nodes[newIndex - 1] = b;
						nodes[newIndex] = a;
					}
					else
						break;
				}
			}
		}

		private void AddNodesBalanced(KdTreeNode<TKey, TValue>[] nodes, int byDimension, int fromIndex, int toIndex)
		{
			if (fromIndex == toIndex)
			{
				Add(nodes[fromIndex].Point, nodes[fromIndex].Value);
				nodes[fromIndex] = null;
				return;
			}

			// 按index排序
			SortNodesArray(nodes, byDimension, fromIndex, toIndex);

			// 查找并添加分割点
			int midIndex = fromIndex + (int)System.Math.Round((toIndex + 1 - fromIndex) / 2f) - 1;

			Add(nodes[midIndex].Point, nodes[midIndex].Value);
			nodes[midIndex] = null;

			// Recurse
			int nextDimension = (byDimension + 1) % dimensions;

			if (fromIndex < midIndex)
				AddNodesBalanced(nodes, nextDimension, fromIndex, midIndex - 1);

			if (toIndex > midIndex)
				AddNodesBalanced(nodes, nextDimension, midIndex + 1, toIndex);
		}

		public void Balance()
		{
			var nodeList = new List<KdTreeNode<TKey, TValue>>();
			AddNodesToList(root, nodeList);

			Clear();

			AddNodesBalanced(nodeList.ToArray(), 0, 0, nodeList.Count - 1);
		}

		private void RemoveChildNodes(KdTreeNode<TKey, TValue> node)
		{
			for (var side = -1; side <= 1; side += 2)
			{
				if (node[side] != null)
				{
					RemoveChildNodes(node[side]);
					node[side] = null;
				}
			}
		}

		public void Clear()
		{
			if (root != null)
				RemoveChildNodes(root);
		}

		public void SaveToFile(string filename)
		{
			BinaryFormatter formatter = new BinaryFormatter();
			using (FileStream stream = File.Create(filename))
			{
				formatter.Serialize(stream, this);
				stream.Flush();
			}
		}

		public static KdTree<TKey, TValue> LoadFromFile(string filename)
		{
			BinaryFormatter formatter = new BinaryFormatter();
			using (FileStream stream = File.Open(filename, FileMode.Open))
			{
				return (KdTree<TKey, TValue>)formatter.Deserialize(stream);
			}

		}

		public IEnumerator<KdTreeNode<TKey, TValue>> GetEnumerator()
		{
			var left = new Stack<KdTreeNode<TKey, TValue>>();
			var right = new Stack<KdTreeNode<TKey, TValue>>();

			void addLeft(KdTreeNode<TKey, TValue> node)
			{
				if (node.LeftChild != null)
				{
					left.Push(node.LeftChild);
				}
			}

			void addRight(KdTreeNode<TKey, TValue> node)
			{
				if (node.RightChild != null)
				{
					right.Push(node.RightChild);
				}
			}

			if (root != null)
			{
				yield return root;

				addLeft(root);
				addRight(root);

				while (true)
				{
					if (left.Any())
					{
						var item = left.Pop();

						addLeft(item);
						addRight(item);

						yield return item;
					}
					else if (right.Any())
					{
						var item = right.Pop();

						addLeft(item);
						addRight(item);

						yield return item;
					}
					else
					{
						break;
					}
				}
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}