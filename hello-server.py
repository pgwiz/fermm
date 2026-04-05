#!/bin/bash
# Simple hello world HTTP server
# Usage: python3 hello-server.py

from http.server import HTTPServer, BaseHTTPRequestHandler

class HelloHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header('Content-Type', 'text/plain; charset=utf-8')
        self.send_header('Access-Control-Allow-Origin', '*')
        self.end_headers()
        self.wfile.write(b'hello world')
    
    def log_message(self, format, *args):
        # Suppress logging
        pass

if __name__ == '__main__':
    server = HTTPServer(('0.0.0.0', 8000), HelloHandler)
    print('Hello world server running on :8000')
    server.serve_forever()
