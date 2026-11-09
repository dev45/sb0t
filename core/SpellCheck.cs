/*
    sb0t ares chat server
    Copyright (C) 2017  AresChat

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using WeCantSpell.Hunspell;

namespace core
{
    class SpellCheck
    {
        private static WordList Engine { get; set; }
        private static readonly HashSet<String> Custom = new HashSet<String>(StringComparer.OrdinalIgnoreCase);

        private static void Init()
        {
            if (Engine != null)
                return;

            Engine = WordList.CreateFromFiles(Path.Combine(AppContext.BaseDirectory, "dictionary.dic"));
            
            Custom.Add("lol");
            Custom.Add("rofl");
            Custom.Add("lmao");
            Custom.Add("lmfao");
            Custom.Add("pmsl");
            Custom.Add("wtf");
            Custom.Add("nvm");
            Custom.Add("irl");
            Custom.Add("ily");
            Custom.Add("bff");
            Custom.Add("tyt");
            Custom.Add("ttyl");
            Custom.Add("brb");
            Custom.Add("bbs");
            Custom.Add("bbl");
            Custom.Add("hb");
            Custom.Add("ty");
            Custom.Add("yw");
            Custom.Add("afaik");
            Custom.Add("afk");
            Custom.Add("idk");
            Custom.Add("bf");
            Custom.Add("gf");
            Custom.Add("btw");
            Custom.Add("np");
            Custom.Add("omg");
            Custom.Add("oic");
            Custom.Add("tmi");
            Custom.Add("asl");
            Custom.Add("ik");
            Custom.Add("ikr");
            Custom.Add("k");
            Custom.Add("plz");
        }

        private static bool IsCorrect(String word)
        {
            return Custom.Contains(word) || Engine.Check(word);
        }

        public static bool Confirm(String text)
        {
            if (String.IsNullOrEmpty(text))
                return false;

            if (Engine == null)
                Init();

            List<String> list = new List<String>(text.Split(new String[] { " " }, StringSplitOptions.RemoveEmptyEntries));

            for (int i = 0; i < list.Count; i++)
                if (!IsCorrect(list[i]))
                    return false;

            return true;
        }

        public static String Correct(String text)
        {
            List<String> list = new List<String>(text.Split(new String[] { " " }, StringSplitOptions.RemoveEmptyEntries));

            if (Engine == null)
                Init();

            for (int i = 0; i < list.Count; i++)
            {
                if (!IsCorrect(list[i]))
                {
                    List<String> suggestions = Engine.Suggest(list[i]).ToList();

                    if (suggestions != null)
                        if (suggestions.Count > 0)
                            list[i] = suggestions[0];
                }
            }

            return String.Join(" ", list.ToArray());
        }

        public static String[] Suggest(String word)
        {
            String str = word.Trim();

            if (!str.Contains(" "))
            {
                if (Engine == null)
                    Init();

                List<String> suggestions = Engine.Suggest(str).ToList();

                if (suggestions != null)
                    return suggestions.ToArray();
            }

            return new String[] { };
        }
    }
}
