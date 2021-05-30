using System;
using System.Collections.Generic;

namespace BDArmory.Evolution
{
    public interface VariantMutation
    {
        public void Apply(ConfigNode craft, VariantEngine engine);
        public Variant GetVariant(string id, string name);
    }
}
