import java.awt.Component;
import java.awt.Container;
import java.awt.Dialog;
import java.awt.Frame;
import java.awt.Window;
import java.awt.event.ActionEvent;
import java.awt.event.KeyEvent;
import java.io.File;
import java.util.Locale;
import javax.swing.AbstractButton;
import javax.swing.Action;
import javax.swing.JComponent;
import javax.swing.KeyStroke;
import javax.swing.SwingUtilities;
import javax.swing.text.JTextComponent;

/** Drives the editor's visible Swing document and its normal Save keyboard shortcut. */
public final class Tur37UyapEditor {
    private static final String Heading = "EMBASIC E-NOTE TUR37";
    private static final String Marker = " TUR37-EDIT";

    public static void main(String[] args) throws Exception {
        try {
            run(args);
            System.exit(0);
        } catch (Throwable error) {
            error.printStackTrace();
            System.exit(1);
        }
    }

    private static void run(String[] args) throws Exception {
        boolean edit = args[0].equals("edit");
        File input = new File(args[1]);
        long previousSize = input.length();
        long previousTime = input.lastModified();
        Thread launcher = new Thread(() -> {
            try {
                Class<?> manager = Class.forName("tr.com.havelsan.uyap.system.editor.common.WPAppManager");
                manager.getMethod("main", String[].class).invoke(null, (Object) new String[] {
                    "getNewWPInstance", "EDITOR_TYPE_DOCUMENT", input.getAbsolutePath()
                });
            } catch (Exception error) {
                throw new IllegalStateException(error);
            }
        });
        launcher.setUncaughtExceptionHandler((thread, error) -> { error.printStackTrace(); System.exit(1); });
        launcher.start();

        JTextComponent[] document = new JTextComponent[1];
        Frame[] frame = new Frame[1];
        long deadline = System.nanoTime() + 45_000_000_000L;
        while (document[0] == null && System.nanoTime() < deadline) {
            SwingUtilities.invokeAndWait(() -> {
                for (Window window : Window.getWindows()) {
                    if (window instanceof Dialog && window.isShowing() &&
                        ((Dialog) window).getTitle().startsWith("Son S")) {
                        window.dispose();
                    }
                }
                for (Frame candidate : Frame.getFrames()) {
                    if (!candidate.isVisible() || !candidate.getTitle().contains(input.getName()) ||
                        !candidate.getTitle().contains("5.4.20")) continue;
                    JTextComponent component = findDocument(candidate);
                    if (component != null) { document[0] = component; frame[0] = candidate; return; }
                }
            });
            if (document[0] == null) Thread.sleep(250);
        }
        if (document[0] == null) throw new IllegalStateException("Visible UYAP 5.4.20 document was not found.");
        SwingUtilities.invokeAndWait(() -> {
            JTextComponent pane = document[0];
            String text = documentText(pane);
            if (!text.contains("\u00c7\u0131\u011f ve \u0130\u011fd\u0131r") ||
                !text.contains("SON PARAGRAF TUR37") || (!edit && !text.contains(Marker)))
                throw new IllegalStateException("The editor text differs from the expected fixture.");
            if (edit) {
                int position = text.indexOf(Heading) + Heading.length();
                pane.setCaretPosition(position);
                pane.replaceSelection(Marker);
                if (!documentText(pane).contains(Heading + Marker))
                    throw new IllegalStateException("The visible editor rejected the change.");
                frame[0].toFront();
                pane.requestFocusInWindow();
            }
        });
        System.out.println("UYAP_VISIBLE_DOCUMENT_OK");

        if (edit) {
            SwingUtilities.invokeAndWait(() -> save(frame[0], document[0]));
            deadline = System.nanoTime() + 20_000_000_000L;
            while (input.length() == previousSize && input.lastModified() == previousTime &&
                   System.nanoTime() < deadline) Thread.sleep(200);
            if (input.length() == previousSize && input.lastModified() == previousTime)
                throw new IllegalStateException("The editor did not save the clone.");
        }
        System.out.println(edit ? "UYAP_EDIT_AND_SAVE_OK" : "UYAP_REOPEN_OK");
    }

    private static JTextComponent findDocument(Component component) {
        if (component instanceof JTextComponent && component.isShowing()) {
            JTextComponent text = (JTextComponent) component;
            if (text.isEditable() && documentText(text).contains(Heading)) return text;
        }
        if (component instanceof Container) {
            for (Component child : ((Container) component).getComponents()) {
                JTextComponent match = findDocument(child);
                if (match != null) return match;
            }
        }
        return null;
    }

    private static String documentText(JTextComponent text) {
        try { return text.getDocument().getText(0, text.getDocument().getLength()); }
        catch (javax.swing.text.BadLocationException error) { throw new IllegalStateException(error); }
    }

    private static void save(Frame frame, JTextComponent pane) {
        AbstractButton saveButton = findSaveButton(frame);
        if (saveButton != null) {
            System.out.println("UYAP_SAVE_TOOLBAR_BUTTON");
            saveButton.doClick();
            return;
        }
        KeyStroke shortcut = KeyStroke.getKeyStroke(KeyEvent.VK_S, java.awt.event.InputEvent.CTRL_DOWN_MASK);
        for (Component component = pane; component != null; component = component.getParent()) {
            if (!(component instanceof JComponent)) continue;
            JComponent swing = (JComponent) component;
            for (int condition : new int[] {JComponent.WHEN_FOCUSED,
                JComponent.WHEN_ANCESTOR_OF_FOCUSED_COMPONENT, JComponent.WHEN_IN_FOCUSED_WINDOW}) {
                Object key = swing.getInputMap(condition).get(shortcut);
                Action action = key == null ? null : swing.getActionMap().get(key);
                if (action != null && action.isEnabled()) {
                    System.out.println("UYAP_SAVE_SWING_SHORTCUT");
                    action.actionPerformed(new ActionEvent(swing, ActionEvent.ACTION_PERFORMED, key.toString()));
                    return;
                }
            }
        }
        throw new IllegalStateException("The official editor Save command was not found.");
    }

    private static AbstractButton findSaveButton(Component component) {
        if (component instanceof AbstractButton && component.isShowing() && component.isEnabled()) {
            AbstractButton button = (AbstractButton) component;
            String tip = button.getToolTipText();
            String name = button.getAccessibleContext() == null ? null : button.getAccessibleContext().getAccessibleName();
            for (String label : new String[] {button.getText(), tip, name, button.getActionCommand()}) {
                if (label == null) continue;
                String lower = label.toLowerCase(Locale.ROOT).trim();
                if (lower.equals("kaydet") || lower.equals("save") ||
                    (lower.contains("ctrl+s") && !lower.contains("farkl") && !lower.contains("save as"))) return button;
            }
        }
        if (component instanceof Container) {
            for (Component child : ((Container) component).getComponents()) {
                AbstractButton match = findSaveButton(child);
                if (match != null) return match;
            }
        }
        return null;
    }
}
