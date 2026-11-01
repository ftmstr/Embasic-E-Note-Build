# Embasic E-Note — security and release state

**TUR 02: NOT A SECURE EDITOR RELEASE.**

The encrypted `.emd` writer/reader, secure key slots, recovery, application
and folder locks, and authenticated exports remain **NOT IMPLEMENTED**.
The WPF window disables secure-note creation and never opens `.emd` content.
The storage navigator may *display filenames* ending in `.emd`, which is
not equivalent to decrypting them.

The tree navigator rejects parent traversal, symlink/junction directory
components, known reserved device names, and paths outside the selected
workspace. These checks do **not** form a complete security boundary
against hostile changes to directory links between check and use; review
TOCTOU and Windows access-control edge cases before protecting secrets.

Plain `.md` is unencrypted. Note contents and keys are not sent to the static
advertising placeholder, and no ad SDK was added.

Security, printing, UDF Editor, DPI/accessibility, Windows GUI, installer,
cryptographic negative tests and recovery acceptance remain **NOT RUN**.
No release package shall be published until the defined P0 gates are met.
