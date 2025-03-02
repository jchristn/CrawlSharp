namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Queued link.
    /// </summary>
    public class QueuedLink
    {
        #region Public-Members

        /// <summary>
        /// URL.
        /// </summary>
        public string Url { get; set; } = null;

        /// <summary>
        /// Parent URL.
        /// </summary>
        public string ParentUrl { get; set; } = null;

        /// <summary>
        /// Depth.
        /// </summary>
        public int Depth
        {
            get
            {
                return _Depth;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(Depth));
                _Depth = value;
            }
        }

        #endregion

        #region Private-Members

        private int _Depth = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Queued link.
        /// </summary>
        public QueuedLink() 
        { 

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
