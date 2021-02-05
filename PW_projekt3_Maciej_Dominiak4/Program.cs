using System;
using System.Diagnostics;
using System.Threading;


// Key == int
using Key = System.Int32;

namespace PW_projekt3_maciej_dominiak
{
    interface Node { }
    interface Info { }

    public enum States
    {
        Clean = 0,
        DFlag = 1,
        IFlag = 2,
        Mark = 3
    }

    class Update
    {
        public States state;
        public Info info;

        public Update(States state, Info info)
        {
            this.state = state;
            this.info = info;
        }
    }

    class Internal : Node
    {
        public Key key;
        public Update update;
        public Node left, right;

        public Internal(int key, Update update, Node left, Node right)
        {
            this.key = key;
            this.update = update;
            this.left = left;
            this.right = right;
        }
    }

    class Leaf : Node
    {
        public Key key;

        public Leaf(int key)
        {
            this.key = key;
        }
    }

    class IInfo : Info
    {
        public Internal p, newInternal;
        public Leaf l;
        public Update myUpdate;

        public IInfo(Internal p, Leaf l, Internal newInternal, Update myUpdate)
        {
            this.p = p;
            this.newInternal = newInternal;
            this.l = l;
            this.myUpdate = myUpdate;
        }
    }

    class DInfo : Info
    {
        public Internal gp, p;
        public Leaf l;
        public Update pupdate, myUpdate;

        public DInfo(Internal gp, Internal p, Leaf l, Update pupdate, Update myUpdate)
        {
            this.gp = gp;
            this.p = p;
            this.l = l;
            this.pupdate = pupdate;
            this.myUpdate = myUpdate;
        }
    }

    class NonBlockingBinaryTree
    {
        private readonly Key Inf1, Inf2;
        private readonly Internal Root;

        public NonBlockingBinaryTree(Key Inf1=-2, Key Inf2=-1)
        {
            if (Inf1 == Inf2)
                throw new Exception("Wrong initialization value");

            if (Inf2 > Inf1)
            {
                this.Inf2 = Inf2;
                this.Inf1 = Inf1;
            }
            else
            {
                this.Inf2 = Inf1;
                this.Inf1 = Inf2;
            }

            Root = new Internal(this.Inf2, new Update(States.Clean, null), new Leaf(this.Inf1), new Leaf(this.Inf2));
        }

        private (Internal, Internal, Leaf, Update, Update) Search(Key k)
        {
            Internal gp = null, p = null;
            Node l = Root;
            Update gpupdate = null, pupdate = null;


            while (l is Internal)
            {
                gp = p;
                p = (Internal)l;
                gpupdate = pupdate;
                pupdate = p.update;

                if (k < ((Internal)l).key)
                    l = p.left;
                else
                    l = p.right;
            }

            return (gp, p, (Leaf)l, pupdate, gpupdate);
        }

        public Leaf Find(Key k)
        {
            Leaf l = null;

            (_, _, l, _, _) = Search(k);

            if (l.key == k)
                return l;

            return null;
        }

        public bool Insert(Key k)
        {
            Internal p = null, newInternal = null;
            Leaf l = null, newSibling = null;
            Leaf nowy = new Leaf(k);
            Update pupdate = null, result = null;
            IInfo op = null;

            while (true)
            {
                (_, p, l, pupdate, _) = Search(k);

                if (l.key == k)
                    return false;

                if (pupdate.state != States.Clean)
                {
                    Help(pupdate);
                }
                else
                {
                    newSibling = new Leaf(l.key);

                    Leaf lewy = null, prawy = null;
                    if (nowy.key < newSibling.key)
                    {
                        lewy = nowy;
                        prawy = newSibling;
                    }
                    else
                    {
                        lewy = newSibling;
                        prawy = nowy;
                    }


                    newInternal = new Internal(Math.Max(k, l.key), new Update(States.Clean, null), lewy, prawy);

                    op = new IInfo(p, l, newInternal, null);
                    op.myUpdate = new Update(States.IFlag, op);

                    result = Interlocked.CompareExchange(ref p.update, op.myUpdate, pupdate);

                    if (result == pupdate)
                    {
                        HelpInsert(op);
                        return true;
                    }
                    else
                    {
                        Help(result);
                    }
                }
            }

        }

        private void HelpInsert(IInfo op)
        {
            CAS_Child(ref op.p, op.l, op.newInternal);
            Interlocked.CompareExchange(ref op.p.update, new Update(States.Clean, op), op.myUpdate);
        }

        public bool Delete(Key k)
        {
            if (k == Inf1 || k == Inf2)
                return false;                   //or throw new Exception("Wrong key");

            Internal gp = null, p = null;
            Leaf l = null;
            Update pupdate = null, gpupdate = null, result = null;
            DInfo op = null;

            while (true)
            {
                (gp, p, l, pupdate, gpupdate) = Search(k);

                if (l.key != k)
                    return false;

                if (gpupdate.state != States.Clean)
                {
                    Help(gpupdate);
                }
                else if (pupdate.state != States.Clean)
                {
                    Help(pupdate);
                }
                else
                {
                    op = new DInfo(gp, p, l, pupdate, null);
                    op.myUpdate = new Update(States.DFlag, op);

                    result = Interlocked.CompareExchange(ref gp.update, op.myUpdate, gpupdate);

                    if (result == gpupdate)
                    {
                        if (HelpDelete(op))
                            return true;
                    }
                    else
                    {
                        Help(result);
                    }
                }
            }
        }

        private bool HelpDelete(DInfo op)
        {
            Update result = null;

            result = Interlocked.CompareExchange(ref op.p.update, new Update(States.Mark, op), op.pupdate);

            if (result == op.pupdate || (result.state == States.Mark && result.info == op))
            {
                HelpMarked(op);
                return true;
            }
            else
            {
                Help(result);
                Interlocked.CompareExchange(ref op.gp.update, new Update(States.Clean, op), op.myUpdate);
                return false;
            }
        }

        private void HelpMarked(DInfo op)
        {
            Node other;

            if (op.p.right == op.l)
                other = op.p.left;
            else
                other = op.p.right;

            CAS_Child(ref op.gp, op.p, other);
            Interlocked.CompareExchange(ref op.gp.update, new Update(States.Clean, op), op.myUpdate);
        }

        private void Help(Update u)
        {
            if (u.state == States.IFlag)
                HelpInsert((IInfo)u.info);
            else if (u.state == States.Mark)
                HelpMarked((DInfo)u.info);
            else if (u.state == States.DFlag)
                HelpDelete((DInfo)u.info);
        }

        private void CAS_Child(ref Internal parent, Node old, Node nowy)
        {
            if (nowy is Leaf)
            {
                if (((Leaf)nowy).key < parent.key)
                    Interlocked.CompareExchange(ref parent.left, nowy, old);
                else
                    Interlocked.CompareExchange(ref parent.right, nowy, old);
            }
            else
            {
                if (((Internal)nowy).key < parent.key)
                    Interlocked.CompareExchange(ref parent.left, nowy, old);
                else
                    Interlocked.CompareExchange(ref parent.right, nowy, old);
            }
        }
    }

    class Program
    {
        static public void PracaDlaWatku(NonBlockingBinaryTree drzewo)
        {
            int praca = 100000;
            int zakres = 1000;

            for (int i = 0; i < praca; i++)
            {
                Random losowanko = new Random();

                if (losowanko.Next(1, 3) == 1)
                    drzewo.Insert(losowanko.Next(1, zakres));
                else
                    drzewo.Delete(losowanko.Next(1, zakres));
            }
        }



        static void Main(string[] args)
        {
            NonBlockingBinaryTree drzewo = new NonBlockingBinaryTree();

            int watki = 100;
            Thread[] tabWatki = new Thread[watki];
            Stopwatch zegar = new Stopwatch();
            zegar.Start();
            for (int i = 0; i < watki; i++)
            {
                tabWatki[i] = new Thread(() => PracaDlaWatku(drzewo));
                tabWatki[i].Start();
            }
            for (int i = 0; i < watki; i++)
            {
                tabWatki[i].Join();
            }
            zegar.Stop();
            Console.WriteLine(zegar.Elapsed);
            Console.ReadKey();
        }
    }
}
