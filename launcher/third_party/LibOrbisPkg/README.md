# LibOrbisPkg

The PKG and PFS reader behind the launcher's **Dump game** page, from
[maxton/LibOrbisPkg](https://github.com/maxton/LibOrbisPkg) (commit `6434772`, LGPL-3.0, see
`LICENSE.txt`). These are the sources of its `LibOrbisPkg.Core` project, unchanged except for one fix.

## The fix

`PFS/PFSCReader.cs` `ReadSector` inflated each compressed 64 KiB block with a single
`DeflateStream.Read` call. Since .NET 6 that call may return fewer bytes than asked for, and the
rest of the block stayed zero: an extracted Bloodborne then had about two thirds of its `.dcx`
archives cut short (the shaders among them, so the game hung on a black screen). The block is now
read until it is full, and a short block is an error instead of silent zeros.
