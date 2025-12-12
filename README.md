基于https://gitee.com/raphaelcheung/zircon-legend-server二次开发，包含了后台管理界面
启动后访问http://[ip]:17080，仅用于学习用途，QQ群 915941142，相关data数据在q群中下载

可下载 <a href="https://github.com/livse000/zircon-legend-server-main/releases/download/%E5%AE%A2%E6%88%B7%E7%AB%AF%E5%90%8C%E6%AD%A5/Legend.exe">客户端</a> 替换本地的客户端exe，会自动同步服务器修改，但会影响带宽，如不需要此功能，也可继续使用原客户端exe，手动将服务器的systemdb覆盖本地的systemdb达到一样的效果

```yaml
version: "3"

services:
  zircon:
    container_name: zircon-server
    image: livse/zirconlegend:latest
    networks:
      - 1panel-network
    ports:
      - "0.0.0.0:17000:7000"   # 游戏端口（映射到宿主机 17000）
      - "0.0.0.0:17080:7080"   # 管理后台端口（映射到宿主机 17080）
    restart: unless-stopped
    user: "0:0"                     # 以 root 用户运行
    volumes:
      - ./datas:/zircon/datas       # 数据持久化
      - /etc/localtime:/etc/localtime:ro    # 时区同步
      - /etc/timezone:/etc/timezone:ro      # 时区配置
    environment:
      - TZ=Asia/Shanghai            # 可选：显式设置时区

networks:
  1panel-network:
    external: true                  # 使用外部网络（1Panel 管理）
```
