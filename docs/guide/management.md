# 管理功能

ACCCOM 提供了书签、预设、快捷键和设置等管理功能，帮助用户组织串口调试工作流。

## 书签管理

`BookmarkManager` 在收发数据中标记关键条目，方便快速定位：

- **添加/删除**：按条目 ID 添加书签，自动记录方向、时间戳、预览文本
- **导航**：支持上下导航跳转到对应书签条目
- **UI 绑定**：与 UI 书签列表双向绑定（`ObservableCollection<BookmarkItem>`）
- **Modbus 支持**：支持 Modbus 事务书签

## 预设管理

`PresetManager` 管理串口配置预设，持久化到 `presets.json`：

- **保存/加载**：保存串口配置组合（端口、波特率、数据位、停止位、校验位、DTR/RTS）
- **快速切换**：快速切换不同设备的配置方案
- **Modbus 支持**：支持 Modbus 配置预设

## 快捷键管理

`ShortcutManager` 管理快捷发送栏项，持久化到 `shortcuts.json`：

- **自定义管理**：支持自定义添加/删除快捷发送指令
- **默认预置**：内置常用 AT 指令（AT、AT+GMR、AT+RST 等）
- **JSON 持久化**：通过 `JsonFilePersistenceManager` 基类实现序列化持久化
- **Modbus 支持**：支持 Modbus 命令预设

## 设置管理

`SettingsService` 管理应用全局设置，持久化到 `settings.json`：

- **加载/保存**：读写 `AppSettings` 配置对象
- **容错机制**：文件损坏时自动回退到默认设置
- **全局配置**：覆盖主题、语言、缓冲区大小等全局选项
- **Modbus 支持**：Modbus 配置保存

## 相关文档

- [自动化系统](../guide/automation.md)
- [串口操作指南](../guide/serial.md)
