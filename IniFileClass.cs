using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtau.Core.Util
{
    /// <summary>
    /// ini文件操作类
    /// </summary>
    class IniFileClass
    {

        public IniFileClass() { }
        public IniFileClass(string szPath)
        {
            m_Path = szPath;
        }
        #region 段信息的获取
        //读取一个ini 文件中的所有段
        [DllImport("kernel32", EntryPoint = "GetPrivateProfileSectionNamesW", CharSet = CharSet.Unicode)]
        private extern static int getSectionNames(
        [MarshalAs(UnmanagedType.LPWStr)] string szBuffer, int nlen, string filename);

        //读取段里的所有数据
        [DllImport("kernel32", EntryPoint = "GetPrivateProfileSectionW", CharSet = CharSet.Unicode)]
        private extern static int getSectionValues(string Section,
        [MarshalAs(UnmanagedType.LPWStr)] string szBuffer, int nlen, string filename);
        #endregion


        #region 键值的获取和设置
        //读取键的整形值
        [DllImport("kernel32", EntryPoint = "GetPrivateProfileIntW", CharSet = CharSet.Unicode)]
        private static extern int getKeyIntValue(string Section, string Key, int nDefault, string FileName);

        //读取字符串键值
        [DllImport("kernel32", EntryPoint = "GetPrivateProfileStringW", CharSet = CharSet.Unicode)]
        private static extern int getKeyValue(string section, string key, string lpDefault,
            string szValue, int nlen, string filename);

        //
        //
        //写字符串键值
        [DllImport("kernel32", EntryPoint = "WritePrivateProfileStringW", CharSet = CharSet.Unicode)]
        private static extern bool setKeyValue(string Section, string key, string szValue, string FileName);

        //写段值
        [DllImport("kernel32", EntryPoint = "WritePrivateProfileSectionW", CharSet = CharSet.Unicode)]
        private static extern bool setSectionValue(string section, string szvalue, string filename);
        #endregion

        private static readonly char[] sept = { '\0' };	//分隔字符

        private string m_Path = null;		//ini文件路径

        /// <summary>
        /// ini文件路径
        /// </summary>
        private string Path
        {
            set { m_Path = value; }
            get { return m_Path; }
        }

        /// <summary>
        /// 读取所有段名
        /// </summary>
        public string[] SectionNames
        {
            get
            {
                string buffer = new string('\0', 32768);
                int nlen = getSectionNames(buffer, 32768 - 1, m_Path) - 1;
                if (nlen > 0)
                {
                    return buffer.Substring(0, nlen).Split(sept);
                }
                return null;
            }
        }

        /// <summary>
        /// 读取段里的数据到一个字符串数组
        /// </summary>
        /// <param name="section">段名</param>
        /// <param name="bufferSize">读取的数据大小(字节)</param>
        /// <returns>成功则不为null</returns>
        public string[] SectionValues(string section, int bufferSize)
        {
            string buffer = new string('\0', bufferSize);
            int nlen = getSectionValues(section, buffer, bufferSize, m_Path) - 1;
            if (nlen > 0)
            {
                return buffer.Substring(0, nlen).Split(sept);
            }
            return null;
        }
        public string[] SectionValues(string section)
        {
            return SectionValues(section, 32768);
        }

        /// <summary>
        /// 从一个段中读取其 键-值 数据
        /// </summary>
        /// <param name="section">段名</param>
        /// <param name="bufferSize">读取的数据大小(字节)</param>
        /// <returns>成功则不为null</returns>
        public Dictionary<string, string> SectionValuesEx(string section, int bufferSize)
        {
            string[] sztmp = SectionValues(section, bufferSize);
            if (sztmp != null)
            {
                int ArrayLen = sztmp.Length;
                if (ArrayLen > 0)
                {
                    Dictionary<string, string> dtRet = new Dictionary<string, string>();
                    for (int i = 0; i < ArrayLen; i++)
                    {
                        int pos1 = sztmp[i].IndexOf('=');
                        if (pos1 > 1)
                        {
                            int nlen = sztmp[i].Length;
                            //	取键名,键值
                            pos1++;
                            if (pos1 < nlen)
                                dtRet.Add(sztmp[i].Substring(0, pos1 - 1), sztmp[i].Substring(pos1, nlen - pos1));
                        }
                    }
                    return dtRet;
                }
            }
            return null;
        }
        public Dictionary<string, string> SectionValuesEx(string section)
        {
            return SectionValuesEx(section, 32768);
        }

        /// <summary>
        /// 写一个段的数据
        /// </summary>
        /// <param name="section"></param>
        /// <param name="szValue">段的数据(如果为null则删除这个段)</param>
        /// <returns>成功则为true</returns>
        public bool setSectionValue(string section, string szValue)
        {
            return setSectionValue(section, szValue, m_Path);
        }

        /// <summary>
        /// 读整形键值
        /// </summary>
        /// <param name="section"></param>
        /// <param name="key"></param>
        /// <returns>成功则不为-1</returns>
        public int getKeyIntValue(string section, string key)
        {
            return getKeyIntValue(section, key, -1, m_Path);
        }

        /// <summary>
        /// 写整形键值
        /// </summary>
        /// <param name="section"></param>
        /// <param name="key"></param>
        /// <param name="dwValue"></param>
        /// <returns>成功则为true</returns>
        public bool setKeyIntValue(string section, string key, int dwValue)
        {
            return setKeyValue(section, key, dwValue.ToString(), m_Path);
        }

        /// <summary>
        /// 读取键值
        /// </summary>
        /// <param name="section"></param>
        /// <param name="key"></param>
        /// <returns>成功则不为null</returns>
        public string getKeyValue(string section, string key)
        {
            string szBuffer = new string('0', 256);
            int nlen = getKeyValue(section, key, string.Empty, szBuffer, 256, m_Path);
            string ret = szBuffer.Substring(0, nlen);
            return ret.Split('\0')[0];
        }

        /// <summary>
        /// 写字符串键值
        /// </summary>
        /// <param name="Section"></param>
        /// <param name="key"></param>
        /// <param name="szValue"></param>
        /// <returns>成功则为true</returns>
        public bool setKeyValue(string Section, string key, string szValue)
        {
            return setKeyValue(Section, key, szValue, m_Path);
        }
    }//end class CIni
}
