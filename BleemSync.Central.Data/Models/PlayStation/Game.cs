﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace BleemSync.Central.Data.Models.PlayStation
{
    [Table("PlayStation_Games")]
    public class Game : BaseGame
    {
        public int MemoryCardBlockCount { get; set; }
        public bool MultitapCompatible { get; set; }
        public bool LinkCableCompatible { get; set; }
        public bool VibrationCompatible { get; set; }
        public bool AnalogCompatible { get; set; }
        public bool DigitalCompatible { get; set; }
        public bool LightGunCompatible { get; set; }
        public virtual ICollection<Disc> Discs { get; set; }
        public virtual ICollection<Art> Art { get; set; }
        public virtual ICollection<GameRevision> Revisions { get; set; }
        public int RevisionId { get; set; }
        public virtual GameRevision Revision { get; set; }

        public Game() : base()
        {
            Discs = new List<Disc>();
            Art = new List<Art>();
        }
    }
}
