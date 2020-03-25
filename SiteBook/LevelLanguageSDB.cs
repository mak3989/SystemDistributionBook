using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SiteBook
{
    public class LevelLanguageSDB
    {
        int LevelL;
        /// <summary>
        /// Язык
        /// </summary>
        public BookSDB.languageEnum Language { get; set; }
        /// <summary>
        /// Уровень владения
        /// </summary>
        public int Level
        {
            get
            {
                return LevelL;
            }
            set
            {
                if (value > 10)
                    LevelL = 10;
                else if (value < 1)
                    LevelL = 1;
                else
                    LevelL = value;
            }
        }
        public LevelLanguageSDB()
        {
        }
        /// <summary>
        /// Конструктор объекта язык-уровень
        /// </summary>
        /// <param name="language"></param>
        /// <param name="level"></param>
        public LevelLanguageSDB(BookSDB.languageEnum language, int level)
        {
            Language = language;
            Level = level;
        }
    }
}
