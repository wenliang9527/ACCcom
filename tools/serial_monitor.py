import tkinter as tk
from tkinter import ttk, scrolledtext
import serial
import serial.tools.list_ports
import threading
import sys
import time

class SerialMonitor:
    def __init__(self, root, port=None, baud=115200):
        self.root = root
        self.root.title(f"串口监视器 - {port or '未连接'}")
        self.root.geometry("800x500")
        self.ser = None
        self.running = False
        self.port = port
        self.baud = baud

        top = ttk.Frame(root)
        top.pack(fill=tk.X, padx=5, pady=5)

        ttk.Label(top, text="端口:").pack(side=tk.LEFT)
        self.port_var = tk.StringVar(value=port or "")
        self.port_combo = ttk.Combobox(top, textvariable=self.port_var, width=12)
        self.port_combo['values'] = [p.device for p in serial.tools.list_ports.comports()]
        self.port_combo.pack(side=tk.LEFT, padx=2)

        ttk.Label(top, text="波特率:").pack(side=tk.LEFT, padx=(10,0))
        self.baud_var = tk.StringVar(value=str(baud))
        baud_combo = ttk.Combobox(top, textvariable=self.baud_var, width=8,
                                  values=['9600','19200','38400','57600','115200','230400','460800','921600'])
        baud_combo.pack(side=tk.LEFT, padx=2)

        self.open_btn = ttk.Button(top, text="打开", command=self.toggle_serial)
        self.open_btn.pack(side=tk.LEFT, padx=10)

        self.clear_btn = ttk.Button(top, text="清屏", command=self.clear_output)
        self.clear_btn.pack(side=tk.LEFT, padx=2)

        self.hex_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(top, text="HEX显示", variable=self.hex_var).pack(side=tk.LEFT, padx=10)

        self.auto_scroll = tk.BooleanVar(value=True)
        ttk.Checkbutton(top, text="自动滚动", variable=self.auto_scroll).pack(side=tk.LEFT)

        self.output = scrolledtext.ScrolledText(root, state=tk.DISABLED, font=('Consolas', 10), bg='#1e1e1e', fg='#d4d4d4', insertbackground='white')
        self.output.pack(fill=tk.BOTH, expand=True, padx=5, pady=(0,5))

        bottom = ttk.Frame(root)
        bottom.pack(fill=tk.X, padx=5, pady=(0,5))

        self.send_entry = tk.Text(bottom, height=3, font=('Consolas', 10))
        self.send_entry.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0,5))
        self.send_entry.bind('<Return>', self.on_send_key)

        send_frame = ttk.Frame(bottom)
        send_frame.pack(side=tk.RIGHT)

        self.send_hex_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(send_frame, text="HEX", variable=self.send_hex_var).pack(anchor=tk.W)

        ttk.Button(send_frame, text="发送", command=self.send_data).pack(fill=tk.X, pady=2)
        ttk.Button(send_frame, text="发送+换行", command=lambda: self.send_data(with_newline=True)).pack(fill=tk.X)

        self.status = ttk.Label(root, text="就绪", anchor=tk.W)
        self.status.pack(fill=tk.X, padx=5, pady=(0,2))

        self.write_buf = []

        if port:
            self.root.after(100, self.open_serial)

        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

    def log(self, text, hex_mode=False):
        self.root.after(0, self._append_text, text, hex_mode)

    def _append_text(self, text, hex_mode):
        self.output.config(state=tk.NORMAL)
        if hex_mode:
            self.output.insert(tk.END, text + ' ', ('hex',))
        else:
            self.output.insert(tk.END, text)
        if self.auto_scroll.get():
            self.output.see(tk.END)
        self.output.config(state=tk.DISABLED)

    def clear_output(self):
        self.output.config(state=tk.NORMAL)
        self.output.delete('1.0', tk.END)
        self.output.config(state=tk.DISABLED)

    def toggle_serial(self):
        if self.ser and self.ser.is_open:
            self.close_serial()
        else:
            self.open_serial()

    def open_serial(self):
        port = self.port_var.get().strip()
        if not port:
            self.status.config(text="请选择端口")
            return
        try:
            baud = int(self.baud_var.get())
        except ValueError:
            self.status.config(text="波特率无效")
            return
        try:
            self.ser = serial.Serial(port, baud, timeout=0.1)
            self.running = True
            self.open_btn.config(text="关闭")
            self.port_combo.config(state=tk.DISABLED)
            self.root.title(f"串口监视器 - {port} ({baud})")
            self.status.config(text=f"已连接 {port} @ {baud}")
            threading.Thread(target=self.read_loop, daemon=True).start()
        except Exception as e:
            self.status.config(text=f"打开失败: {e}")

    def close_serial(self):
        self.running = False
        if self.ser:
            try:
                self.ser.close()
            except:
                pass
            self.ser = None
        self.open_btn.config(text="打开")
        self.port_combo.config(state=tk.NORMAL)
        self.root.title("串口监视器 - 未连接")
        self.status.config(text="已断开")

    def read_loop(self):
        hex_mode = False
        while self.running and self.ser and self.ser.is_open:
            try:
                if self.hex_var.get() != hex_mode:
                    hex_mode = self.hex_var.get()
                if hex_mode:
                    data = self.ser.read(1024)
                    if data:
                        hex_str = ' '.join(f'{b:02X}' for b in data)
                        self.log(hex_str + ' ', hex_mode=True)
                else:
                    line = self.ser.readline()
                    if line:
                        try:
                            text = line.decode('utf-8', errors='replace')
                        except:
                            text = str(line)
                        self.log(text)
            except serial.SerialException:
                break
            except Exception:
                break
        self.root.after(0, self.close_serial)

    def on_send_key(self, event):
        if not event.state & 0x0001:
            self.send_data(with_newline=True)
            return 'break'

    def send_data(self, with_newline=False):
        if not self.ser or not self.ser.is_open:
            self.status.config(text="未连接")
            return
        text = self.send_entry.get('1.0', 'end-1c')
        if not text:
            return
        try:
            if self.send_hex_var.get():
                hex_str = text.replace(' ', '').replace('\n', '').replace('\r', '')
                data = bytes.fromhex(hex_str)
            else:
                data = (text + '\n').encode('utf-8') if with_newline else text.encode('utf-8')
            self.ser.write(data)
            self.send_entry.delete('1.0', tk.END)
            self.status.config(text=f"已发送 {len(data)} 字节")
        except Exception as e:
            self.status.config(text=f"发送失败: {e}")

    def on_close(self):
        self.running = False
        if self.ser:
            try:
                self.ser.close()
            except:
                pass
        self.root.destroy()

if __name__ == '__main__':
    port = sys.argv[1] if len(sys.argv) > 1 else None
    baud = int(sys.argv[2]) if len(sys.argv) > 2 else 115200
    root = tk.Tk()
    SerialMonitor(root, port, baud)
    root.mainloop()
