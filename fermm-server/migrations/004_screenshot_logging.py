"""Add screenshot logging table

Revision ID: 004_screenshot_logging
Create Date: 2026-03-30

"""
from alembic import op
import sqlalchemy as sa
from sqlalchemy.sql import func

# revision identifiers
revision = '004_screenshot_logging'
down_revision = '003_task_queue' 
branch_labels = None
depends_on = None

def upgrade():
    # Create screenshots table
    op.create_table('screenshots',
        sa.Column('id', sa.String(), nullable=False),
        sa.Column('device_id', sa.String(), nullable=False),
        sa.Column('filename', sa.String(), nullable=False),
        sa.Column('filepath', sa.String(), nullable=False),
        sa.Column('file_size', sa.Integer(), nullable=False),
        sa.Column('width', sa.Integer(), nullable=True),
        sa.Column('height', sa.Integer(), nullable=True),
        sa.Column('active_window_title', sa.String(), nullable=True),
        sa.Column('active_process_name', sa.String(), nullable=True),
        sa.Column('active_process_id', sa.Integer(), nullable=True),
        sa.Column('capture_method', sa.String(), nullable=False, default='manual'),
        sa.Column('captured_by', sa.String(), nullable=True),
        sa.Column('tags', sa.JSON(), nullable=True),
        sa.Column('notes', sa.Text(), nullable=True),
        sa.Column('captured_at', sa.DateTime(timezone=True), server_default=func.now()),
        sa.Column('metadata', sa.JSON(), nullable=True),
        sa.ForeignKeyConstraint(['device_id'], ['devices.id'], ),
        sa.PrimaryKeyConstraint('id')
    )
    
    # Create indexes for better query performance
    op.create_index('ix_screenshots_device_id', 'screenshots', ['device_id'])
    op.create_index('ix_screenshots_captured_at', 'screenshots', ['captured_at'])
    op.create_index('ix_screenshots_captured_by', 'screenshots', ['captured_by'])

def downgrade():
    op.drop_index('ix_screenshots_captured_by', 'screenshots')
    op.drop_index('ix_screenshots_captured_at', 'screenshots')
    op.drop_index('ix_screenshots_device_id', 'screenshots')
    op.drop_table('screenshots')